using System;
using UnityEngine;

[Serializable]
public struct TradeOffer
{
    public bool active;
    public int offerId;

    public int fromPlayerId;   // proposer (must be current turn player)
    public int toPlayerId;     // -1 = any player can accept, else specific target

    public ResourceType giveType;
    public int giveAmount;

    public ResourceType getType;
    public int getAmount;

    public static TradeOffer None => new TradeOffer { active = false, offerId = 0, toPlayerId = -1 };
}