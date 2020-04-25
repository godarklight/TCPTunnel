using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

public class ReliableHandler
{
    private long INITIAL_RETRANSMIT = TimeSpan.TicksPerMillisecond * 5;
    private long RETRANSMIT_INTERVAL = TimeSpan.TicksPerMillisecond * 100;
    private long RETRANSMIT_QUEUE = 512 * 1024;
    private int freeSendFragment = 0;
    private Dictionary<int, byte[]> heldMessages = new Dictionary<int, byte[]>();
    private ConcurrentQueue<SendFragment> udpTransmit = new ConcurrentQueue<SendFragment>();
    private ConcurrentQueue<SendFragment> udpInitialRetransmit = new ConcurrentQueue<SendFragment>();
    private ConcurrentQueue<SendFragment> udpRetransmit = new ConcurrentQueue<SendFragment>();
    public int retransmitBytes = 0;
    private ConcurrentQueue<byte[]> tcpTransmit = new ConcurrentQueue<byte[]>();
    private int clientID;
    private int receiveSequence = Int32.MaxValue;
    private int lastACK = Int32.MaxValue;
    public long lastUDPSend = 0;
    public long lastUDPReceive = 0;
    public long lastTCPReceive = 0;

    public ReliableHandler(int clientID)
    {
        this.clientID = clientID;
    }

    public void HandleUDPMessage(byte[] data, int length, int sequence, int ack)
    {
        lastUDPReceive = DateTime.UtcNow.Ticks;
        int sequenceToReceive = SequenceToReceive();
        if (AckGreaterThan(ack, lastACK))
        {
            lastACK = ack;
        }
        //Only ACK, no data.
        if (sequence == -1)
        {
            return;
        }
        byte[] queueData = new byte[length];
        Array.Copy(data, 16, queueData, 0, length);
        if (sequenceToReceive == sequence)
        {
            tcpTransmit.Enqueue(queueData);
            if (heldMessages.ContainsKey(sequence))
            {
                heldMessages.Remove(sequence);
            }
            //Give the chain of unbroken messages to TCP as well            
            receiveSequence = sequence;
            sequenceToReceive = SequenceToReceive();
            while (heldMessages.ContainsKey(sequenceToReceive))
            {
                byte[] queueData2 = heldMessages[sequenceToReceive];
                heldMessages.Remove(sequenceToReceive);
                tcpTransmit.Enqueue(queueData2);
                receiveSequence = sequenceToReceive;
                sequenceToReceive = SequenceToReceive();
            }
        }
        else
        {
            //Future message
            if (AckGreaterThan(sequence, sequenceToReceive))
            {
                if (!heldMessages.ContainsKey(sequence))
                {
                    heldMessages.Add(sequence, queueData);
                }
            }
        }
    }

    public void HandleTCPData(byte[] data, int length)
    {
        lastTCPReceive = DateTime.UtcNow.Ticks;
        int fragmentStart = 0;
        int fragmentLeft = length;
        while (fragmentLeft > 0)
        {
            int thisFragment = fragmentLeft;
            if (thisFragment > 500)
            {
                thisFragment = 500;
            }
            SendFragment sf = new SendFragment();
            sf.sequence = freeSendFragment++;
            sf.data = new byte[thisFragment];
            Array.Copy(data, fragmentStart, sf.data, 0, thisFragment);
            udpTransmit.Enqueue(sf);
            fragmentStart += thisFragment;
            fragmentLeft -= thisFragment;
        }
    }

    public byte[] SendToTCP()
    {
        if (tcpTransmit.TryDequeue(out byte[] sendData))
        {
            return sendData;
        }
        return null;
    }

    public void SendToUDP(out int sendSequence, out int sendACK, out byte[] sendData)
    {
        long currentTime = DateTime.UtcNow.Ticks;
        //Send new messages if the retransmit queue is small
        if (retransmitBytes < RETRANSMIT_QUEUE)
        {
            if (udpTransmit.TryDequeue(out SendFragment firstDequeueMessage))
            {
                firstDequeueMessage.nextSendTime = currentTime + INITIAL_RETRANSMIT;
                udpInitialRetransmit.Enqueue(firstDequeueMessage);
                retransmitBytes += firstDequeueMessage.data.Length;
                sendSequence = firstDequeueMessage.sequence;
                sendACK = receiveSequence;
                sendData = firstDequeueMessage.data;
                return;
            }
        }
        //Retransmit old messages
        while (true)
        {
            if (udpRetransmit.TryPeek(out SendFragment peekMessage))
            {
                if (currentTime < peekMessage.nextSendTime)
                {
                    //The queue is in order, all messages are waiting to be transmitted
                    break;
                }
                if (udpRetransmit.TryDequeue(out SendFragment dequeueMessage))
                {
                    //Don't send and requeue the message if we've gotten an ack for it
                    if (AckGreaterThan(dequeueMessage.sequence, lastACK))
                    {
                        dequeueMessage.nextSendTime = currentTime + RETRANSMIT_INTERVAL;
                        udpRetransmit.Enqueue(dequeueMessage);
                        sendSequence = dequeueMessage.sequence;
                        sendACK = receiveSequence;
                        sendData = dequeueMessage.data;
                        return;
                    }
                    else
                    {
                        retransmitBytes -= dequeueMessage.data.Length;
                    }
                }
            }
            else
            {
                //No messages in retransmit queue
                break;
            }
        }
        //Retransmit initial messages
        while (true)
        {
            if (udpInitialRetransmit.TryPeek(out SendFragment peekMessage))
            {
                if (currentTime < peekMessage.nextSendTime)
                {
                    //The queue is in order, all messages are waiting to be transmitted
                    break;
                }
                if (udpInitialRetransmit.TryDequeue(out SendFragment dequeueMessage))
                {
                    dequeueMessage.nextSendTime = currentTime + RETRANSMIT_INTERVAL;
                    udpRetransmit.Enqueue(dequeueMessage);
                    sendSequence = dequeueMessage.sequence;
                    sendACK = receiveSequence;
                    sendData = dequeueMessage.data;
                    return;
                }
            }
            else
            {
                //No messages in retransmit queue
                break;
            }
        }
        //Nothing ready in retransmit queue, send new message
        if (udpTransmit.TryDequeue(out SendFragment firstDequeueMessage2))
        {
            firstDequeueMessage2.nextSendTime = currentTime + RETRANSMIT_INTERVAL;
            udpRetransmit.Enqueue(firstDequeueMessage2);
            retransmitBytes += firstDequeueMessage2.data.Length;
            sendSequence = firstDequeueMessage2.sequence;
            sendACK = receiveSequence;
            sendData = firstDequeueMessage2.data;
            return;
        }
        //No message to send
        sendData = null;
        sendACK = receiveSequence;
        sendSequence = -1;
    }

    private bool AckGreaterThan(int lhs, int rhs)
    {
        int distance = Math.Abs(lhs - rhs);
        bool distanceBig = distance > Int32.MaxValue / 4;
        if (distanceBig)
        {
            return lhs < rhs;
        }
        return lhs > rhs;
    }

    private int SequenceToReceive()
    {
        int sequenceToReceive = receiveSequence + 1;
        if (sequenceToReceive < 0)
        {
            sequenceToReceive = 0;
        }
        return sequenceToReceive;
    }
}