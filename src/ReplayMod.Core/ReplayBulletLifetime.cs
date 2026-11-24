using UnityEngine;

namespace ReplayMod.Core;

public class ReplayBulletLifetime : MonoBehaviour
{
    private void OnDestroy()
    {
        var sim = GetComponent<BulletSim>();
        if (sim == null) return;
        foreach (var bullet in sim.bullets) bullet.Remove();
        sim.bullets.Clear();
    }
}
