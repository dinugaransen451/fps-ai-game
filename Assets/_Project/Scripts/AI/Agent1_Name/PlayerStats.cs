using UnityEngine;

// Stand-in for the player's health and ammo, so the Director has something to watch.
public class PlayerStats : MonoBehaviour
{
    public float health = 100f;
    public float maxHealth = 100f;
    public int ammo = 30;
    public int fullAmmo = 30;
}