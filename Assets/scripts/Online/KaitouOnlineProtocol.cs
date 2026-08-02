using System;
using UnityEngine;

namespace KaitouOnline
{
    public enum MessageType
    {
        None,
        SeatAssigned,
        LobbyState,
        StartGame,
        ActionRequest,
        StateSnapshot,
        SecurityDiceState,
        TreasureDisplayChoice,
        TreasureStealChoice,
        TreasureRobberDisplayChoice,
        ArrestResolutionState,
        VictoryResolutionState,
        RestartGame,
        TestGrantAllSpecial,
        HoneyTrapStatus,
        DetectiveChoice,
        AppraiserTypeChoice,
        AppraiserOrder,
        FrameUpChoice,
        AnalysisTreasureChoice,
        PrivateState,
        GameOver,
        ReturnToLobby,
        TurnCleanup,
        StateCheckpointResult,
        DesyncDetected,
        PlayerNameUpdate
    }

    [Serializable]
    public struct Envelope
    {
        public int version;
        public MessageType type;
        public int senderSeat;
        public int targetSeat;
        public int sequence;
        public string payload;
    }

    [Serializable]
    public struct LobbyState
    {
        public int playerCount;
        public int connectedPlayers;
        public int confirmedHumanPlayers;
        public int cpuPlayers;
        public bool participantsConfirmed;
        public bool gameStarted;
        public string[] playerNames;
    }

    [Serializable]
    public struct SeatAssignment
    {
        public int seat;
        public bool host;
    }

    [Serializable]
    public struct StartGameState
    {
        public string sceneName;
        public int playerCount;
        public int humanPlayerCount;
        public int cpuPlayers;
        public int randomSeed;
    }

    [Serializable]
    public struct ActionRequest
    {
        public string action;
        public int actorSeat;
        public int cardId;
        public int targetCardId;
        public int number;
        public string data;
    }

    [Serializable]
    public struct ActionCardChoice
    {
        public int day;
        public int seat;
        public int specialEffect;
        public bool isExhibit;
        public bool isThief;
        public bool isCage;
        public int declaredNumber;
    }

    [Serializable]
    public struct ActionSelectionState
    {
        public int day;
        public double revealAtServerTime;
        public ActionCardChoice[] choices;
    }

    [Serializable]
    public struct SecurityDiceState
    {
        public int day;
        public int result;
    }

    [Serializable]
    public struct TreasureDisplayChoice
    {
        public int day;
        public int actorSeat;
        public int treasureId;
    }

    [Serializable]
    public struct TreasureStealChoice
    {
        public int day;
        public int actorSeat;
        public int treasureId;
    }

    [Serializable]
    public struct ArrestResolutionState
    {
        public int day;
        public bool[] arrested;
        public bool[] eliminated;
        public bool[] criminalRecords;
        public int[] prisonUntilDays;
        public int[] successfulCageSeats;
        public int penaltySeed;
    }

    [Serializable]
    public struct VictoryResolutionState
    {
        public int day;
        public bool gameOver;
        public int[] winnerSeats;
        public int[] revealSeats;
    }

    [Serializable]
    public struct HoneyTrapStatus
    {
        public bool active;
        public int robberSeat;
        public int victimSeat;
    }

    [Serializable]
    public struct OnlineSeatChoice
    {
        public int day;
        public int actorSeat;
        public int value;
    }

    [Serializable]
    public struct AppraiserOrderState
    {
        public int day;
        public int actorSeat;
        public int[] treasureIds;
    }

    [Serializable]
    public struct StateCheckpoint
    {
        public int day;
        public string checkpoint;
        public int actorSeat;
        public string signature;
        public bool success;
        public string message;
    }

    [Serializable]
    public struct OnlinePlayerName
    {
        public int seat;
        public string value;
    }

    public static class Protocol
    {
        // オンライン進行規約。古いMacビルドとの混在を防ぐため、
        // フェーズ同期方式を変更したら必ず更新する。
        public const int Version = 14;
        public const string MessageName = "KaitouOnlineEvent";

        public static string Json<T>(T value) => JsonUtility.ToJson(value);
        public static T Parse<T>(string json) =>
            string.IsNullOrEmpty(json) ? default : JsonUtility.FromJson<T>(json);
    }
}
