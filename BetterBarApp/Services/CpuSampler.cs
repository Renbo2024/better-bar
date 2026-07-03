using System.Runtime.InteropServices;

namespace BetterBarApp.Services;

/// <summary>
/// Samples per-logical-processor CPU usage (0..1 per thread) using
/// NtQuerySystemInformation(SystemProcessorPerformanceInformation) — no perf-counter
/// dependency. Usage is computed from the idle/total time deltas between calls.
/// </summary>
public sealed class CpuSampler
{
    private readonly int    _count;
    private readonly long[] _prevIdle;
    private readonly long[] _prevTotal;
    private readonly float[] _usage;

    public int ThreadCount => _count;

    public CpuSampler()
    {
        _count     = Math.Max(1, Environment.ProcessorCount);
        _prevIdle  = new long[_count];
        _prevTotal = new long[_count];
        _usage     = new float[_count];
        Sample();   // prime the deltas
    }

    /// <summary>Returns the latest per-thread usage (0..1). Reuses the same array.</summary>
    public float[] Sample()
    {
        int sz = Marshal.SizeOf<SystemProcessorPerformanceInformation>();
        IntPtr buf = Marshal.AllocHGlobal(sz * _count);
        try
        {
            if (NtQuerySystemInformation(SystemProcessorPerformanceInformation_Class, buf, sz * _count, out _) != 0)
                return _usage;

            for (int i = 0; i < _count; i++)
            {
                var s     = Marshal.PtrToStructure<SystemProcessorPerformanceInformation>(buf + i * sz);
                long idle = s.IdleTime;
                long tot  = s.KernelTime + s.UserTime;   // KernelTime includes idle
                long dIdle = idle - _prevIdle[i];
                long dTot  = tot  - _prevTotal[i];
                _prevIdle[i]  = idle;
                _prevTotal[i] = tot;
                _usage[i] = dTot > 0 ? Math.Clamp(1f - (float)((double)dIdle / dTot), 0f, 1f) : 0f;
            }
        }
        catch { /* leave previous usage */ }
        finally { Marshal.FreeHGlobal(buf); }
        return _usage;
    }

    /// <summary>Overall usage (average across threads), 0..1.</summary>
    public float Overall()
    {
        if (_usage.Length == 0) return 0f;
        float sum = 0; foreach (var u in _usage) sum += u;
        return sum / _usage.Length;
    }

    private const int SystemProcessorPerformanceInformation_Class = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessorPerformanceInformation
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);
}
