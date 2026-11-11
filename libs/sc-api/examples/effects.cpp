#include <sc-api/api.h>
#include <sc-api/device_info.h>
#include <sc-api/events.h>
#include <sc-api/ffb.h>
#include <sc-api/sim_data.h>
#include <sc-api/telemetry.h>
#include <sc-api/time.h>

#include <cassert>
#include <cmath>
#include <iostream>
#include <thread>

#include "effects.h"

using namespace sc_api::usage;

using sc_api::device_info::DeviceInfo;
using sc_api::device_info::DeviceRole;
using sc_api::device_info::FeedbackType;

constexpr char* LOG_HEADER     = "[SC-LINK API] ";
constexpr float PI             = 3.1415;
constexpr float ONE_OVER_PI    = 1.f / PI;
constexpr float ONE_OVER_PI_SQ = 1.f / PI / PI;

sc_api::Api                              api_thread;
std::unique_ptr<sc_api::Api::EventQueue> event_queue;
std::shared_ptr<sc_api::Session>         session;
sc_api::DeviceSessionId                  brake_ap;
sc_api::DeviceSessionId                  throttle_ap;
std::unique_ptr<sc_api::FfbPipeline>     pipeline_brake;
std::unique_ptr<sc_api::FfbPipeline>     pipeline_throttle;
bool                                     is_brake_configured        = false;
bool                                     is_throttle_configured     = false;
bool                                     is_brake_playing_effect    = false;
bool                                     is_throttle_playing_effect = false;
PeriodicEffectType                       periodic_effect_type       = PeriodicEffectType::Sine;
float                                    periodic_effect_frequency  = 20.0f;

extern "C" _declspec(dllexport) Device Init(long timeout_s)
{
    Device result = Device::None;

    event_queue = api_thread.createEventQueue();

    sc_api::ApiUserInformation api_user_information;
    api_user_information.display_name   = "driving-lab";
    api_user_information.type           = "";
    api_user_information.path           = "";
    api_user_information.author         = "Simucube | Tampere University";
    api_user_information.version_string = "";

    sc_api::NoAuthControlEnabler control_enabler(
        &api_thread,
        sc_api::Session::control_ffb_effects,
        "driving-lab",
        api_user_information);

    auto timeout = std::chrono::steady_clock::now() + std::chrono::seconds(timeout_s);

     while (auto opt_event = event_queue->tryPopUntil(timeout)) {
        const sc_api::Event event = *opt_event;

        // Wait for session to connect and control to be available
        if (auto* s = sc_api::event::getIfSessionStateChanged(&event)) {
            if (s->session && (s->control_flags & sc_api::Session::control_ffb_effects) != 0u) {
                session = s->session;
            }
        };

        if (session) {
            auto device_info = session->getDeviceInfo();
            for (const DeviceInfo& device : *device_info) {
                if (device.getRole() == DeviceRole::brake_pedal) {
                    brake_ap = device.getSessionId();
                    result   = (Device)(result | Device::BrakePedal);
                } else if (device.getRole() == DeviceRole::throttle_pedal) {
                    throttle_ap = device.getSessionId();
                    result      = (Device)(result | Device::ThrottlePedal);
                } else if (device.getRole() == DeviceRole::wheelbase) {
                    result = (Device)(result | Device::WheelBase);
                } else if (device.getRole() == DeviceRole::wheel) {
                    result = (Device)(result | Device::Wheel);
                }
            }
        }
    }

    if (!session) {
        //std::cout << LOG_HEADER << "Could not form SC-API session" << std::endl;
        return result;
    }

    /* auto device_info = session->getDeviceInfo();
    for (const DeviceInfo& device : *device_info) {
        std::cout << "Device UID: " << device.getUid()
                  << " Session id: " << device.getSessionId().id
                  << " role: " << toString(device.getRole())
                  << std::endl;
    }*/

    //std::this_thread::sleep_for(std::chrono::seconds(1));

    return result;
}

extern "C" _declspec(dllexport) void Configure(
    Device pedal,
    sc_api::OffsetType offset_type)
{
    if ((pedal & Device::BrakePedal) != 0 && brake_ap) {
        sc_api::PipelineConfig config_brake;
        config_brake.offset_type = offset_type;
        pipeline_brake           = std::make_unique<sc_api::FfbPipeline>(session, brake_ap);
        pipeline_brake->configure(config_brake);
        is_brake_configured = true;
    }

    if ((pedal & Device::ThrottlePedal) != 0 && throttle_ap) {
        sc_api::PipelineConfig config_throttle;
        config_throttle.offset_type = offset_type;
        pipeline_throttle           = std::make_unique<sc_api::FfbPipeline>(session, throttle_ap);
        pipeline_throttle->configure(config_throttle);
        is_throttle_configured = true;
    }
}

extern "C" _declspec(dllexport) void ConfigurePeriodic(
    PeriodicEffectType type,
    float frequency) {
    periodic_effect_type = type;
    periodic_effect_frequency = frequency;
}

extern "C" _declspec(dllexport) void Run(
    Device pedal,
    EffectType effect_type,
    int duration_ms,
    float amplitude)
{
    if (is_brake_playing_effect || is_throttle_playing_effect) {
        return;
    }

    is_brake_playing_effect    = (pedal & Device::BrakePedal) != 0 && pipeline_brake;
    is_throttle_playing_effect = (pedal & Device::ThrottlePedal) != 0 && pipeline_throttle;

    if (!is_brake_playing_effect && !is_throttle_playing_effect) {
        return;
    }

    auto start_time         = sc_api::Clock::now();

    // 1000Hz update rate
    // Usually this should match what ever is the simulation rate of the system
    auto update_rate        = std::chrono::milliseconds(1);

    // Give 5ms time for the samples to reach the pedal and to give some time for pedal to interpolate between
    // separate samples to make transitions smooth. Using lower values often works but if provided samples don't
    // overlap smoothly, there is risk that there are glitches when pedal runs out of samples to play or sample
    // arrives so late that there isn't any more time to do as smooth linear interpolation as was intended
    auto sample_time_offset = std::chrono::milliseconds(5);

    // This determines how long sample will play if there are not additional samples provided or following
    // samples take too long to arrive (eg. PC lags)
    auto sample_length      = std::chrono::milliseconds(10);

    const auto duration     = std::chrono::milliseconds(duration_ms);
    const auto start        = std::chrono::steady_clock::now();
    const auto timeout      = start + duration;

    float w                 = periodic_effect_frequency * PI * 2;

    while (std::chrono::steady_clock::now() - start < duration) {
        while (auto event = event_queue->tryPop()) {
            if (auto* s = sc_api::event::getIfSessionStateChanged(&event)) {
                if (s->state != sc_api::SessionState::connected_control) {
                    //std::cerr << LOG_HEADER << "Session was disconnected." << std::endl;
                    goto finished;
                }
            }
        }

        // If we would generate multiple different samples with lower update rate, it would be better to calculate
        // sample start time based on the end time of the previous sent sample set
        auto cur_time = sc_api::Clock::now();
        double seconds_from_start =
            std::chrono::duration_cast<std::chrono::duration<double>>(cur_time - start_time).count();

        float value   = 0;
        switch (effect_type) {
            case EffectType::Constant:
                value = amplitude;
                break;
            case EffectType::Periodic:
                switch (periodic_effect_type)
                { 
                    case PeriodicEffectType::Sine:
                        value = (float)std::sin(w * seconds_from_start);
                        break;
                    case PeriodicEffectType::Triangle:
                        value = 8.f * ONE_OVER_PI_SQ * (float)(
                            std::sin(w * seconds_from_start) - 
                            std::sin(3 * w * seconds_from_start) / 9 +
                            std::sin(5 * w * seconds_from_start) / 25);
                        break;
                    case PeriodicEffectType::Square:
                        value = (float)fabs(std::sin(w * seconds_from_start + PI / 4)) < 0.5 ? -1.f : 1.f;
                        break;
                    case PeriodicEffectType::SawTooth:
                        value = 2.f * ONE_OVER_PI * (float)(
                            std::sin(w * seconds_from_start) - 
                            std::sin(2 * w * seconds_from_start) / 2 +
                            std::sin(3 * w * seconds_from_start) / 3 -
                            std::sin(4 * w * seconds_from_start) / 4 +
                            std::sin(5 * w * seconds_from_start) / 5);
                        break;
                }
                value *= amplitude;
                break;
        }

        // Use two samples because using only one sample would produce sawtooth pattern if the next sample arrives after
        // this sample start time Linear interpolation will try to interpolate samples so that the first sample value
        // will be reached at exactly the given start timestamp (so the interpolation starts before the start timestamp)
        // and after last samples timestamp offset will be interpolated towards 0 offset if there are no more samples
        // available
        static constexpr uint32_t k_sample_count          = 2;
        float                     samples[k_sample_count] = {value, value};

        if (is_brake_playing_effect && pipeline_brake) {
            pipeline_brake->generateEffect(cur_time + sample_time_offset, sample_length, samples, k_sample_count);
        }
        if (is_throttle_playing_effect && pipeline_throttle) {
            pipeline_throttle->generateEffect(cur_time + sample_time_offset, sample_length, samples, k_sample_count);
        }

        if (!is_brake_playing_effect && !is_throttle_playing_effect) {
            break;
        }

        // Do some busy looping while we wait for the next update time
        // This is stupid way to do this, but it works for this. Proper use cases should use timer to handle effect
        // generation
        while (sc_api::Clock::now() < (cur_time + update_rate)) {
            std::this_thread::yield();
        }
    }

finished:

    is_brake_playing_effect    = false;
    is_throttle_playing_effect = false;
}

extern "C" _declspec(dllexport) void Stop(Device pedal) {
    if ((pedal & Device::BrakePedal) != 0)
        is_brake_playing_effect = false;
    if ((pedal & Device::ThrottlePedal) != 0)
        is_throttle_playing_effect = false;
}