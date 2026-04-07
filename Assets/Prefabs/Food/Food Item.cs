using Unity.Netcode;
using UnityEngine;

public class FoodItem : NetworkBehaviour
{
    public override void OnNetworkDespawn()
    {
        gameObject.SetActive(false);
    }
}