#include <iostream>

#include "effects.h"

using namespace sc_api::usage;

int main()
{
    std::cout << "Initializing...\n";

    Pedal pedals = Init(3);

    std::cout << "Init returned: " << static_cast<int>(pedals) << '\n';

    if (pedals & Pedal::Brake) std::cout << "Brake available\n";

    if (pedals & Pedal::Throttle) std::cout << "Throttle available\n";

    if (pedals != Pedal::None)
    {
        Configure(Pedal::Both, OffsetType::ForceN);

        std::cout << "Running...\n";
        Run(Pedal::Both, EffectType::Periodic, 1000, 2.0f);
    }

    std::cout << "Done.\n";

	return 0;
}
