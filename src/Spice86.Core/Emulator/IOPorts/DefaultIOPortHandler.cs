﻿namespace Spice86.Core.Emulator.IOPorts;

using System.Numerics;
using System.Runtime.CompilerServices;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;

/// <summary>
/// Abstract base class for all classes that handle port reads and writes. Provides a default implementation for handling unhandled ports.
/// </summary>
public abstract class DefaultIOPortHandler : IIOPortHandler {
    /// <summary>
    /// Contains the argument of the last <see cref="ReadByte"/> operation.
    /// </summary>
    public int LastPortRead { get; protected set; }

    /// <summary>
    /// Contains the first argument of the last <see cref="WriteByte"/> operation.
    /// </summary>
    public int LastPortWritten { get; protected set; }

    /// <summary>
    /// Contains the second argument of the last <see cref="WriteByte"/> operation.
    /// </summary>
    public int LastPortWrittenValue { get; protected set; }

    /// <summary>
    /// The logger service implementation.
    /// </summary>
    protected readonly ILoggerService _loggerService;

    /// <summary>
    /// Whether we raise an exception when a port wasn't handled.
    /// </summary>
    protected bool _failOnUnhandledPort;

    /// <summary>
    /// The CPU state.
    /// </summary>
    protected readonly State _state;

    /// <summary>
    /// Constructor for DefaultIOPortHandler
    /// </summary>
    /// <param name="state">The CPU Registers and Flags.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">Logger service implementation.</param>
    protected DefaultIOPortHandler(State state, bool failOnUnhandledPort, ILoggerService loggerService) {
        _loggerService = loggerService;
        _state = state;
        _failOnUnhandledPort = failOnUnhandledPort;
    }

    /// <summary>
    /// Updates the <see cref="LastPortRead"/> for the internal UI debugger.
    /// </summary>
    /// <param name="port">The port number</param>
    protected void UpdateLastPortRead(int port) {
        LastPortRead = port;
    }

    /// <summary>
    /// Updates the <see cref="LastPortWritten"/> and value for the internal UI debugger.
    /// </summary>
    /// <param name="port">The port number</param>
    /// <param name="value">The value written to the port.</param>
    protected void UpdateLastPortWrite(int port, int value) {
        LastPortWritten = port;
        LastPortWrittenValue = value;
    }

    /// <summary>
    /// Read a byte from the specified port.
    /// </summary>
    /// <param name="port">The port to read from.</param>
    /// <returns>The value read from the port.</returns>
    public virtual byte ReadByte(int port) {
        LogUnhandledPortRead(port);
        return OnUnandledIn(port);
    }

    /// <summary>
    /// Logs that an unhandled port read error occured.
    /// </summary>
    /// <param name="port">The port number that was read.</param>
    /// <param name="methodName">The name of the calling method. Automatically populated if not specified.</param>
    protected void LogUnhandledPortRead(int port, [CallerMemberName] string? methodName = null) {
        if (_failOnUnhandledPort && _loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Unhandled port read: 0x{PortNumber:X4} in {MethodName}", port, methodName);
        }
    }


    /// <summary>
    /// Logs that an unhandled port write error occured.
    /// </summary>
    /// <param name="port">The port number that was written.</param>
    /// <param name="value">The value that was supposed to be written to the port.</param>
    /// <param name="methodName">The name of the calling method. Automatically populated if not specified.</param>
    protected void LogUnhandledPortWrite<T>(int port, T value, [CallerMemberName] string? methodName = null)
        where T : INumber<T> {
        if (_failOnUnhandledPort && _loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Unhandled port write: 0x{PortNumber:X4}, 0x{Value:X4} in {MethodName}", port, value,
                methodName);
        }
    }

    /// <summary>
    /// Reads a word from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>The value read from the port.</returns>
    public virtual ushort ReadWord(int port) {
        LogUnhandledPortRead(port);
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }

        return ushort.MaxValue;
    }

    /// <summary>
    /// Reads a double word from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>The value read from the port.</returns>
    public virtual uint ReadDWord(int port) {
        LogUnhandledPortRead(port);
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }

        return uint.MaxValue;
    }

    /// <summary>
    /// Writes a byte to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <param name="value">The value to write to the port.</param>
    public virtual void WriteByte(int port, byte value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <summary>
    /// Writes a word to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <param name="value">The value to write to the port.</param>
    public virtual void WriteWord(int port, ushort value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <summary>
    /// Writes a double word to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <param name="value">The value to write to the port.</param>
    public virtual void WriteDWord(int port, uint value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <summary>
    /// Invoked when an unhandled input operation is performed on a port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>A default value.</returns>
    protected virtual byte OnUnandledIn(int port) {
        LogUnhandledPortRead(port);
        OnUnhandledPort(port);
        return byte.MaxValue;
    }

    /// <summary>
    /// Invoked when an unhandled port operation is performed.
    /// </summary>
    /// <param name="port">The port number.</param>
    protected virtual void OnUnhandledPort(int port) {
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }
    }
}