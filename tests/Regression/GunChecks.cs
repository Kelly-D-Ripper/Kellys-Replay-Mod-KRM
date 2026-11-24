using System.Text;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class GunChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("resolved gun bullet roundtrip preserves native address and actual spread", () => {
            var sample = new GunBulletEvent { Time = 7.25, unitId = 64, station = 3, weaponSlot = 2, partId = 11, partGunSlot = 1,
                weaponName = "Cannon Å", position = new GlobalPosition(123, 456, -789), velocity = new Vector3(910.25f, -2.75f, 117.5f),
                life = 3.5f, tracerSize = .5f, reliability = .12f, color = new Color(.1f, .2f, .3f, .4f),
                tracer = true, active = true, impacted = true, explosive = true, sound = false };
            using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) sample.Write(writer);
            stream.Position = 0; var result = new GunBulletEvent(); using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) result.Read(reader);
            Check(stream.Position == stream.Length, "complete codec");
            Check(result.station == 3 && result.weaponSlot == 2 && result.partId == 11 && result.partGunSlot == 1 && result.weaponName == sample.weaponName, "address");
            Check(result.unitId == 64 && result.Time == 7.25 && result.position.z == -789 && result.velocity.x == 910.25f && result.velocity.y == -2.75f && result.life == 3.5f, "native projectile state");
            Check(result.tracer && result.active && result.impacted && result.explosive && !result.sound && result.reliability == .12f && result.color.a == .4f, "flags and tracer");
            result.Reset(); Check(result.station == ushort.MaxValue && result.partId == -1 && result.weaponName == "" && !result.sound && result.life == 0, "pooled reset");
        });
        test("resolved gun identity codec bounds malformed addresses and names", () => {
            foreach (var sample in new[] { new GunBulletEvent {unitId=1}, new GunBulletEvent { unitId=1, station = 256 }, new GunBulletEvent { unitId=1, station = 1, partId = 256 },
                new GunBulletEvent { unitId=1, station = 1, weaponName = new string('x', 513) } })
            {
                using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
                Throws<InvalidDataException>(() => sample.Write(writer));
            }
            using var truncated = new MemoryStream();
            using (var writer = new BinaryWriter(truncated, Encoding.UTF8, true))
            { writer.Write(1d); writer.Write(1u); writer.Write((ushort)1); writer.Write((ushort)0); writer.Write((short)-1); writer.Write((ushort)0); writer.Write((ushort)10); writer.Write((byte)65); }
            truncated.Position = 0; using var reader = new BinaryReader(truncated); Throws<EndOfStreamException>(() => new GunBulletEvent().Read(reader));
            var mutations = new Action<GunBulletEvent>[] {
                e=>e.unitId=0, e=>e.Time=double.NaN, e=>e.Time=double.PositiveInfinity, e=>e.Time=-1,
                e=>e.position.x=float.NaN, e=>e.position.y=float.PositiveInfinity, e=>e.position.z=float.NegativeInfinity,
                e=>e.velocity.x=float.NaN, e=>e.velocity.y=float.PositiveInfinity, e=>e.velocity.z=float.NegativeInfinity,
                e=>e.life=float.NaN, e=>e.life=float.PositiveInfinity, e=>e.life=-1,
                e=>e.tracerSize=float.NaN, e=>e.tracerSize=float.PositiveInfinity, e=>e.tracerSize=-1,
                e=>e.reliability=float.NaN, e=>e.reliability=float.PositiveInfinity, e=>e.reliability=-.1f, e=>e.reliability=1.1f,
                e=>e.color.r=float.NaN, e=>e.color.g=float.PositiveInfinity, e=>e.color.b=float.NegativeInfinity, e=>e.color.a=float.NaN
            };
            foreach (var mutation in mutations)
            {
                var value = ValidBullet(); mutation(value); Check(!value.HasValidPayload(), "invalid capture rejected before queue");
                using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
                Throws<InvalidDataException>(()=>value.Write(writer)); Check(output.Length==0,"writer validates before any bytes");
                // Corrupt an otherwise complete native-format body, bypassing
                // production Write so the reader must independently reject it.
                byte[] corrupt = UncheckedPayload(value); using var input = new MemoryStream(corrupt); using var corruptReader = new BinaryReader(input);
                Throws<InvalidDataException>(()=>new GunBulletEvent().Read(corruptReader));
            }
            var hdr = ValidBullet(); hdr.color = new Color(3, 2, 1, .5f); hdr.reliability=1; hdr.life=0; hdr.tracerSize=0;
            Check(hdr.HasValidPayload(),"finite HDR colours, native reliability endpoint and expired shot remain valid");
        });
        test("gun burst time follows replay seconds and coalesces legacy same-frame muzzles", () => {
            var burst = new ReplayGunBurstState(); Check(double.IsPositiveInfinity(burst.Age(12)), "fresh gun is not firing");
            Check(burst.Record(12, true), "first legacy muzzle"); Check(!burst.Record(12, true), "same-frame legacy muzzle does not restart audio");
            Check(burst.Record(12, false), "new explicitly recorded shot in same timestamp group retained");
            Check(burst.Age(12.1) > .099 && burst.Age(12.1) < .101, "burst expiry uses replay time");
            Check(burst.Age(12) == 0, "pause does not age burst");
            Check(Math.Abs(ReplayGunBurstState.NativeAge(.12, .02f, .002f) - .102) < .00001, "slow motion preserves native fixed-step burst tolerance");
            Check(Math.Abs(ReplayGunBurstState.NativeAge(.12, .02f, .04f) - .12) < .00001, "low FPS does not subtract negative grace");
            Check(!burst.Record(double.NaN, false) && !burst.Record(double.PositiveInfinity, false) && burst.LastShot == 12, "invalid time cannot poison native audio clock");
        });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static GunBulletEvent ValidBullet() => new GunBulletEvent { Time=1,unitId=1,station=1,life=1,tracerSize=1,reliability=.5f,color=new Color(1,1,1,1) };
    private static byte[] UncheckedPayload(GunBulletEvent e)
    {
        using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,Encoding.UTF8,true))
        {
            w.Write(e.Time);w.Write(e.unitId);w.Write(e.station);w.Write(e.weaponSlot);w.Write(e.partId);w.Write(e.partGunSlot);w.Write((ushort)0);
            w.Write(e.position.x);w.Write(e.position.y);w.Write(e.position.z);w.Write(e.velocity.x);w.Write(e.velocity.y);w.Write(e.velocity.z);
            w.Write(e.life);w.Write(e.tracerSize);w.Write(e.reliability);w.Write(e.color.r);w.Write(e.color.g);w.Write(e.color.b);w.Write(e.color.a);
            w.Write(e.tracer);w.Write(e.active);w.Write(e.impacted);w.Write(e.explosive);w.Write(e.sound);
        }
        return stream.ToArray();
    }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
