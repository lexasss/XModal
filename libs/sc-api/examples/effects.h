#pragma once

namespace sc_api::usage {

enum Device {
    None          = 0,
    BrakePedal    = 0x01,
    ThrottlePedal = 0x02,
    BothPedals    = BrakePedal | ThrottlePedal,
    AnyPedal      = BothPedals,
    WheelBase     = 0x11,
    Wheel         = 0x12,
};

enum class EffectType {
    Constant = 0,
    Periodic = 1
};

enum class OffsetType {
	TorqueNm       = 0,
	TorqueRelative = 1,
	ForceN         = 2,
	ForceRelative  = 3,
	PositionMm     = 4
};

enum class PeriodicEffectType {
	Sine     = 0,
	Triangle = 1,
	Square   = 2,
	SawTooth = 3
};

extern "C" {
__declspec(dllexport) Device Init(long timeout_s = 2);
__declspec(dllexport) void Configure(Device pedal, OffsetType offset_type);
__declspec(dllexport) void ConfigurePeriodic(PeriodicEffectType type, float frequency);
__declspec(dllexport) void Run(Device pedal, EffectType effectType, int durationMs, float amplitude);
__declspec(dllexport) void Stop(Device pedal);
}

}