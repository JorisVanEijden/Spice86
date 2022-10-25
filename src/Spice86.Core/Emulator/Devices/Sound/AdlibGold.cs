namespace Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Adlib Gold implementation
/// </summary>
public class AdlibGold : DefaultIOPortHandler {

    public AdlibGold(Machine machine, Configuration configuration) : base(machine, configuration) {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FM_MUSIC_DATA_PORT_NUMBER_2, this);
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FM_MUSIC_STATUS_PORT_NUMBER_2, this);
    }
}