using System;
using System.Threading.Tasks;
using Steamworks;
using SteamNetworkLib.Core;
using SteamNetworkLib.Models;
using SteamNetworkLib.Utilities;

public static class RuntimeChecks
{
    public static int Run()
    {
        int checks = 0;
        void Require(bool ok, string description)
        {
            if (!ok) throw new InvalidOperationException(description);
            checks++;
        }
        var peer = new CSteamID(76561198000000001UL);
        using var manager = new SteamP2PManager(new NetworkRules());
        byte[]? retained = null;
        int callbacks = 0;
        manager.ConfigureOverrides((target, data, channel, sendType) =>
        {
            Require(target == peer && channel == 2 && sendType == EP2PSend.k_EP2PSendReliable, "packet metadata");
            retained = data;
            data[0] = 65;
            Require(Convert.ToBase64String(data) == "QQID", "callback encoding");
            callbacks++;
            return Task.FromResult(true);
        }, null, () => peer, () => true);
        byte[] original = { 1, 2, 3 };
        Require(manager.SendPacketAsync(peer, original, 2).GetAwaiter().GetResult(), "send result");
        Require(callbacks == 1 && original[0] == 65 && ReferenceEquals(original, retained), "callback preserves packet alias");
        GC.Collect(); GC.WaitForPendingFinalizers();
        Require(retained![0] == 65, "retained packet after managed collection");

        int handled = 0;
        using var subscription = manager.SubscribeMessageHandler<TextMessage>((message, sender) =>
        {
            Require(sender == peer && message.SenderId == peer && message.Content == "compiler round trip", "received text payload");
            handled++;
        });
        manager.ConfigureOverrides((target, data, channel, sendType) =>
        {
            Require(MessageSerializer.IsValidMessage(data) && MessageSerializer.GetMessageType(data) == "TEXT", "serialized envelope");
            manager.ProcessExternalPacket(peer, data, channel);
            return Task.FromResult(true);
        }, null, () => peer, () => true);
        Require(manager.SendMessageAsync(peer, new TextMessage { Content = "compiler round trip" }).GetAwaiter().GetResult(), "message send");
        Require(handled == 1, "handler called once");
        subscription.Dispose();
        Require(manager.SendMessageAsync(peer, new TextMessage { Content = "compiler round trip" }).GetAwaiter().GetResult(), "message after unsubscribe");
        Require(handled == 1, "subscription removal");

        manager.ConfigureOverrides((target, data, channel, sendType) =>
        {
            Require(MessageSerializer.IsValidMessage(data), "binary message envelope");
            manager.ProcessExternalPacket(peer, data, channel);
            return Task.FromResult(true);
        }, null, () => peer, () => true);

        byte[] chunk = { 0, 127, 255, 42 };
        int filesHandled = 0;
        using var fileSubscription = manager.SubscribeMessageHandler<FileTransferMessage>((message, sender) =>
        {
            Require(sender == peer && message.TransferId == "transfer-1" && message.FileName == "sample.bin" &&
                message.FileSize == 8 && message.ChunkIndex == 1 && message.TotalChunks == 2 && message.IsFileData,
                "file transfer metadata");
            Require(message.ChunkData.Length == 4 && message.ChunkData[0] == 0 && message.ChunkData[2] == 255 &&
                message.ChunkData[3] == 42, "binary chunk payload");
            Require(!ReferenceEquals(chunk, message.ChunkData), "deserialization owns a new chunk");
            message.ChunkData[0] = 99;
            filesHandled++;
        });
        Require(manager.SendMessageAsync(peer, new FileTransferMessage
        {
            TransferId = "transfer-1", FileName = "sample.bin", FileSize = 8, ChunkIndex = 1,
            TotalChunks = 2, IsFileData = true, ChunkData = chunk
        }).GetAwaiter().GetResult(), "file transfer send");
        Require(filesHandled == 1, "file transfer handler called once");
        Require(chunk[0] == 0, "received chunk mutation leaves sender storage unchanged");

        int streamsHandled = 0;
        using var streamSubscription = manager.SubscribeMessageHandler<StreamMessage>((message, sender) =>
        {
            Require(sender == peer && message.StreamType == "data" && message.StreamId == "stream-1" &&
                message.SequenceNumber == 42 && message.RecommendedSendType == EP2PSend.k_EP2PSendReliable,
                "stream metadata and enum");
            Require(message.StreamData.Length == 4 && message.StreamData[1] == 127 && message.StreamData[2] == 255,
                "stream binary payload");
            Require(message.AckForSequence == 41 && message.IsRetransmit && message.Priority == 3,
                "stream nullable sequence and flags");
            streamsHandled++;
        });
        Require(manager.SendMessageAsync(peer, new StreamMessage
        {
            StreamType = "data", StreamId = "stream-1", SequenceNumber = 42, StreamData = chunk,
            RecommendedSendType = EP2PSend.k_EP2PSendReliable, AckForSequence = 41,
            IsRetransmit = true, Priority = 3
        }).GetAwaiter().GetResult(), "stream message send");
        Require(streamsHandled == 1, "stream handler called once");

        var malformedFile = new FileTransferMessage();
        malformedFile.Deserialize(new byte[] { 255, 255, 255, 127 });
        Require(malformedFile.ChunkData.Length == 0, "oversized binary header is ignored");
        var malformedStream = new StreamMessage();
        malformedStream.Deserialize(new byte[] { 1, 2 });
        Require(malformedStream.StreamData.Length == 0, "truncated binary header is ignored");
#if SCHEDULE_ONE_INTEGRATION
        var audioObject = new UnityEngine.GameObject("S1Interop audio buffer contract");
        AudioStreamingExample.StreamingAudioBuffer? audioBuffer = null;
        try
        {
            var audio = audioObject.AddComponent<UnityEngine.AudioSource>();
            audio.mute = true;
            audioBuffer = new AudioStreamingExample.StreamingAudioBuffer(audio, 8000, 1);
            Require(audio.clip != null && audio.clip.frequency == 8000 && audio.clip.channels == 1, "unchanged streaming buffer constructs a native PCM callback");
            audioBuffer.AddAudioData(new float[] { 0.25f, -0.5f });
            float[] samples = new float[4];
            audio.clip.InvokePCMReaderCallback_Internal(samples);
            Require(samples[0] == 0.25f && samples[1] == -0.5f && samples[2] == 0 && samples[3] == 0, "PCM callback writes through the caller array");
            audio.clip.InvokePCMReaderCallback_Internal(samples);
            Require(samples[0] == 0 && samples[1] == 0, "PCM callback consumes the buffer and pads silence");
            audioBuffer.AddAudioData(new float[] { 0.75f });
            audioBuffer.Reset();
            audio.clip.InvokePCMReaderCallback_Internal(samples);
            Require(samples[0] == 0, "streaming buffer reset clears pending samples");
        }
        finally
        {
            audioBuffer?.Dispose();
            UnityEngine.Object.Destroy(audioObject);
        }
#endif
        return checks;
    }
}
