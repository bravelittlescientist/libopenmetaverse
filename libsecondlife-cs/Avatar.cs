/*
 * Copyright (c) 2006, Second Life Reverse Engineering Team
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without 
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the Second Life Reverse Engineering Team nor the names 
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF 
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
 * POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Timers;
using System.Net;
using System.Collections;

namespace libsecondlife
{
	public class Avatar
	{
		public LLUUID ID;
		public string Name;
	}

	public class MainAvatar
	{
		public LLUUID ID;
		public string FirstName;
		public string LastName;
		public string TeleportMessage;

		private SecondLife Client;
		private int TeleportStatus;
		private Timer TeleportTimer;
		private bool TeleportTimeout;

		public MainAvatar(SecondLife client)
		{
			Client = client;
			TeleportMessage = "";

			// Setup the callbacks
			PacketCallback callback = new PacketCallback(TeleportHandler);
			Client.Network.InternalCallbacks["TeleportStart"] = callback;
			Client.Network.InternalCallbacks["TeleportProgress"] = callback;
			Client.Network.InternalCallbacks["TeleportFailed"] = callback;
			Client.Network.InternalCallbacks["TeleportFinish"] = callback;

			TeleportTimer = new Timer(8000);
			TeleportTimer.Elapsed += new ElapsedEventHandler(TeleportTimerEvent);
			TeleportTimeout = false;
		}

		public bool Teleport(U64 regionHandle, LLVector3 position, out string message)
		{
			TeleportStatus = 0;
			LLVector3 lookAt = new LLVector3(position.X + 1.0F, position.Y, position.Z);

			Hashtable blocks = new Hashtable();
			Hashtable fields = new Hashtable();
			fields["RegionHandle"] = regionHandle;
			fields["LookAt"] = lookAt;
			fields["Position"] = position;
			blocks[fields] = "Info";
			fields = new Hashtable();
			fields["AgentID"] = Client.Network.AgentID;
			fields["SessionID"] = Client.Network.SessionID;
			blocks[fields] = "AgentData";
			Packet packet = PacketBuilder.BuildPacket("TeleportLocationRequest", Client.Protocol, blocks);

			Helpers.Log("Teleporting to region " + regionHandle.ToString(), Helpers.LogLevel.Info);

			// Start the timeout check
			TeleportTimeout = false;
			TeleportTimer.Start();

			Client.Network.SendPacket(packet);

			while (TeleportStatus == 0 && !TeleportTimeout)
			{
				Client.Tick();
			}

			TeleportTimer.Stop();

			if (TeleportTimeout)
			{
				message = "Teleport timed out";
			}
			else
			{
				message = TeleportMessage;
			}

			return (TeleportStatus == 1);
		}

		private void TeleportHandler(Packet packet, Circuit circuit)
		{
			ArrayList blocks;

			if (packet.Layout.Name == "TeleportStart")
			{
				TeleportMessage = "Teleport started";
			}
			else if (packet.Layout.Name == "TeleportProgress")
			{
				blocks = packet.Blocks();

				foreach (Block block in blocks)
				{
					foreach (Field field in block.Fields)
					{
						if (field.Layout.Name == "Message")
						{
							TeleportMessage = System.Text.Encoding.UTF8.GetString((byte[])field.Data).Replace("\0", "");
							return;
						}
					}
				}
			}
			else if (packet.Layout.Name == "TeleportFailed")
			{
				blocks = packet.Blocks();

				foreach (Block block in blocks)
				{
					foreach (Field field in block.Fields)
					{
						if (field.Layout.Name == "Reason")
						{
							TeleportMessage = System.Text.Encoding.UTF8.GetString((byte[])field.Data).Replace("\0", "");
							TeleportStatus = -1;
							return;
						}
					}
				}
			}
			else if (packet.Layout.Name == "TeleportFinish")
			{
				TeleportMessage = "Teleport finished";

				ushort port = 0;
				IPAddress ip = null;
				U64 regionHandle;

				blocks = packet.Blocks();

				foreach (Block block in blocks)
				{
					foreach (Field field in block.Fields)
					{
						if (field.Layout.Name == "SimPort")
						{
							port = (ushort)field.Data;
						}
						else if (field.Layout.Name == "SimIP")
						{
							ip = (IPAddress)field.Data;
						}
						else if (field.Layout.Name == "RegionHandle")
						{
							regionHandle = (U64)field.Data;
						}
					}
				}

				if (Client.Network.Connect(ip, port, Client.Network.CurrentCircuit.CircuitCode, true))
				{
					// Move the avatar in to this sim
					Packet movePacket = PacketBuilder.CompleteAgentMovement(Client.Protocol, Client.Network.AgentID,
						Client.Network.SessionID, Client.Network.CurrentCircuit.CircuitCode);
					Client.Network.SendPacket(movePacket);

					Helpers.Log("Connected to new sim " + Client.Network.CurrentCircuit.ipEndPoint.ToString(), 
						Helpers.LogLevel.Info);

					// Sleep a little while so we can collect parcel information
					System.Threading.Thread.Sleep(1000);

					TeleportStatus = 1;
					return;
				}
				else
				{
					TeleportStatus = -1;
					return;
				}
			}
		}

		private void TeleportTimerEvent(object source, System.Timers.ElapsedEventArgs ea)
		{
			TeleportTimeout = true;
		}
	}
}
