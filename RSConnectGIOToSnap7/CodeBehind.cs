/*=============================================================================|
|  PROJECT RSConnectGIOToSnap7                                           1.0.0 |
|==============================================================================|
|  Copyright (C) 2016 Denis FRAIPONT                                           |
|  All rights reserved.                                                        |
|==============================================================================|
|  RSConnectGIOToSnap7 is free software: you can redistribute it and/or modify |
|  it under the terms of the Lesser GNU General Public License as published by |
|  the Free Software Foundation, either version 3 of the License, or           |
|  (at your option) any later version.                                         |
|                                                                              |
|  It means that you can distribute your commercial software which includes    |
|  RSConnectGIOToSnap7 without the requirement to distribute the source code   |
|  of your application and without the requirement that your application be    |
|  itself distributed under LGPL.                                              |
|                                                                              |
|  RSConnectGIOToSnap7 is distributed in the hope that it will be useful,      |
|  but WITHOUT ANY WARRANTY; without even the implied warranty of              |
|  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the               |
|  Lesser GNU General Public License for more details.                         |
|                                                                              |
|  You should have received a copy of the GNU General Public License and a     |
|  copy of Lesser GNU General Public License along with RSConnectGIOToSnap7.   |
|  If not, see  http://www.gnu.org/licenses/                                   |
|                                                                              |
|  This project uses Sharp7 from Snap7 project: http://snap7.sourceforge.net/  |
|=============================================================================*/

using System;
using System.Collections.Generic;
using System.Text;

using ABB.Robotics.Math;
using ABB.Robotics.RobotStudio;
using ABB.Robotics.RobotStudio.Stations;

using Sharp7;

namespace RSConnectGIOToSnap7
{
	/// <summary>
	/// Code-behind class for the RSConnectGIOToSnap7 Smart Component.
	/// </summary>
	/// <remarks>
	/// The code-behind class should be seen as a service provider used by the 
	/// Smart Component runtime. Only one instance of the code-behind class
	/// is created, regardless of how many instances there are of the associated
	/// Smart Component.
	/// Therefore, the code-behind class should not store any state information.
	/// Instead, use the SmartComponent.StateCache collection.
	/// </remarks>
	public class CodeBehind : SmartComponentCodeBehind
	{
		private S7Client client;
		private bool bCanConnect = false;
		//private bool bRename = false;
		private bool bPLC_AddrIsValid = false;
		private bool bGI_FirstByteAddressIsValid = false;
		private bool bGO_FirstByteAddressIsValid = false;

		/// <summary>
		/// Called from [!:SmartComponent.InitializeCodeBehind]. 
		/// </summary>
		/// <param name="component">Smart Component</param>
		public override void OnInitialize(SmartComponent component)
		{
			///Never Called???
			base.OnInitialize(component);
			CheckClientAndValues(component);

			UpdateGICount(component, 0);
			UpdateGOCount(component, 0);
		}

		/// <summary>
		/// Called when the library or station containing the SmartComponent has been loaded 
		/// </summary>
		/// <param name="component">Smart Component</param>
		public override void OnLoad(SmartComponent component)
		{
			base.OnLoad(component);
			CheckClientAndValues(component);
			Disconnect(component);

			//component.Properties not initialized yet;
			UpdateGICount(component, 0);
			UpdateGOCount(component, 0);
		}

		/// <summary>
		/// Called when the value of a dynamic property value has changed.
		/// </summary>
		/// <param name="component"> Component that owns the changed property. </param>
		/// <param name="changedProperty"> Changed property. </param>
		/// <param name="oldValue"> Previous value of the changed property. </param>
		public override void OnPropertyValueChanged(SmartComponent component, DynamicProperty changedProperty, Object oldValue)
		{
			base.OnPropertyValueChanged(component, changedProperty, oldValue);

			if (changedProperty.Name == "GI_ByteNumber")
			{
				UpdateGICount(component, (int)oldValue);
			}
			if (changedProperty.Name == "GO_ByteNumber")
			{
				UpdateGOCount(component, (int)oldValue);
			}

			//Mark sure client is initialized before connect.
			CheckClientAndValues(component);
		}

		/// <summary>
		/// Called when the value of an I/O signal value has changed.
		/// </summary>
		/// <param name="component"> Component that owns the changed signal. </param>
		/// <param name="changedSignal"> Changed signal. </param>
		public override void OnIOSignalValueChanged(SmartComponent component, IOSignal changedSignal)
		{
			if (changedSignal.Name == "Connect")
			{
				//Mark sure client is initialized before connect.
				CheckClientAndValues(component);

				if (((int)changedSignal.Value != 0) && bCanConnect)
					Connect(component);
				else
					Disconnect(component);
			}
			if ((changedSignal.Name == "Read") && ((int)changedSignal.Value != 0))
			{
				Read(component);
			}
			if (changedSignal.Name.Contains("GO_"))
			{
				if (client.Connected)
				{
					int offset = -1;
					int.TryParse(Right(changedSignal.Name, changedSignal.Name.Length - 3), out offset);
					if (offset >= 0)
					{
						S7Client.S7DataItem item = new S7Client.S7DataItem();
						if (GetS7DataItem((string)component.Properties["GO_FirstByteAddress"].Value, ref item))
						{
							item.Start += offset;
							byte[] b = new byte[1];
							byte.TryParse((string)changedSignal.Value.ToString(), out b[0]);
							int result = 0x01700000;// S7Consts.errCliInvalidBlockType
							switch (item.Area)
							{
								case 0x81: result = client.EBWrite(item.Start, 1, b); //S7Consts.S7AreaPE
									break;
								case 0x83: result = client.MBWrite(item.Start, 1, b); //S7Consts.S7AreaMK
									break;
								case 0x84: result = client.DBWrite(item.DBNumber, item.Start, 1, b); //S7Consts.S7AreaDB
									break;
							}
							ShowResult(component, result);
						}
					}
				}
			}
		}

		/// <summary>
		/// Called during simulation.
		/// </summary>
		/// <param name="component"> Simulated component. </param>
		/// <param name="simulationTime"> Time (in ms) for the current simulation step. </param>
		/// <param name="previousTime"> Time (in ms) for the previous simulation step. </param>
		/// <remarks>
		/// For this method to be called, the component must be marked with
		/// simulate="true" in the xml file.
		/// </remarks>
		public override void OnSimulationStep(SmartComponent component, double simulationTime, double previousTime)
		{
			Read(component);
		}

		/// <summary>
		/// Called to validate the value of a dynamic property with the CustomValidation attribute.
		/// </summary>
		/// <param name="component">Component that owns the changed property.</param>
		/// <param name="property">Property that owns the value to be validated.</param>
		/// <param name="newValue">Value to validate.</param>
		/// <returns>Result of the validation. </returns>
		public override ValueValidationInfo QueryPropertyValueValid(SmartComponent component, DynamicProperty property, object newValue)
		{
			bCanConnect = false;
			if (property.Name == "PLC_Addr")
			{
				bPLC_AddrIsValid = false;
				System.Net.IPAddress ip;
				if (!System.Net.IPAddress.TryParse((string)newValue, out ip))
					return new ValueValidationInfo(ValueValidationResult.InvalidSyntax);
				bPLC_AddrIsValid = true;
			}
			if (property.Name == "GI_FirstByteAddress")
			{
				bGI_FirstByteAddressIsValid = false;
				S7Client.S7DataItem item = new S7Client.S7DataItem();
				if (!GetS7DataItem((string)newValue, ref item))
					return new ValueValidationInfo(ValueValidationResult.InvalidSyntax);
				if (item.WordLen != S7Consts.S7WLByte)
					return new ValueValidationInfo(ValueValidationResult.InvalidSyntax);
				if ((item.Area != S7Consts.S7AreaPA)
					&& (item.Area != S7Consts.S7AreaMK)
					&& (item.Area != S7Consts.S7AreaDB)
					)
					return new ValueValidationInfo(ValueValidationResult.InvalidSyntax);

				bGI_FirstByteAddressIsValid = true;
			}
			if (property.Name == "GO_FirstByteAddress")
			{
				bGO_FirstByteAddressIsValid = false;
				S7Client.S7DataItem item = new S7Client.S7DataItem();
				if (!GetS7DataItem((string)newValue, ref item))
					return new ValueValidationInfo(ValueValidationResult.InvalidSyntax);
				if (item.WordLen != S7Consts.S7WLByte)
					return new ValueValidationInfo(ValueValidationResult.InvalidSyntax);
				if ((item.Area != S7Consts.S7AreaPE)
					&& (item.Area != S7Consts.S7AreaMK)
					&& (item.Area != S7Consts.S7AreaDB)
					)
					return new ValueValidationInfo(ValueValidationResult.InvalidSyntax);

				bGO_FirstByteAddressIsValid = true;
			}

			bCanConnect = bPLC_AddrIsValid;
			bCanConnect &= bGI_FirstByteAddressIsValid || ((int)component.Properties["GI_ByteNumber"].Value == 0);
			bCanConnect &= bGO_FirstByteAddressIsValid || ((int)component.Properties["GO_ByteNumber"].Value == 0);
			component.IOSignals["Connect"].UIVisible = bCanConnect;
			return ValueValidationInfo.Valid;
		}

		/// <summary>
		/// Mark sure client is initialized.
		/// </summary>
		/// <param name="component"></param>
		private void CheckClientAndValues(SmartComponent component)
		{
			if (client == null)
			{
				client = new S7Client();
				Disconnect(component);
			}
			for (int i = 0; i < component.Properties.Count; ++i)
			{
				component.Properties[i].ValidateValue(component.Properties[i].Value);
			}
		}

		/// <summary>
		/// Connect component to PLC
		/// </summary>
		/// <param name="component"></param>
		private void Connect(SmartComponent component)
		{
			int result;
			string ip = (string)component.Properties["PLC_Addr"].Value;
			int rack = (int)component.Properties["PLC_Rack"].Value;
			int slot = (int)component.Properties["PLC_Slot"].Value;
			result = client.ConnectTo(ip, rack, slot);
			ShowResult(component, result);
			UpdateConnected(component, result == 0);
		}

		/// <summary>
		/// Disconnect component to PLC
		/// </summary>
		/// <param name="component"></param>
		private void Disconnect(SmartComponent component)
		{
			client.Disconnect();
			component.IOSignals["Connect"].Value = 0;
			UpdateConnected(component, false);
		}

		/// <summary>
		/// Update each property depends connection status
		/// </summary>
		/// <param name="component"></param>
		/// <param name="bConnected"></param>
		private void UpdateConnected(SmartComponent component, Boolean bConnected)
		{
			component.Properties["Status"].Value = bConnected ? "Connected" : "Disconnected";

			component.Properties["PLC_Addr"].ReadOnly = bConnected;
			component.Properties["PLC_Rack"].ReadOnly = bConnected;
			component.Properties["PLC_Slot"].ReadOnly = bConnected;

			component.Properties["GI_ByteNumber"].ReadOnly = bConnected;
			component.Properties["GI_FirstByteAddress"].ReadOnly = bConnected;
			component.Properties["GO_ByteNumber"].ReadOnly = bConnected;
			component.Properties["GO_FirstByteAddress"].ReadOnly = bConnected;

			int giCount = (int)component.Properties["GI_ByteNumber"].Value;
			component.IOSignals["Read"].UIVisible = (giCount > 0) && bConnected;
		}

		/// <summary>
		/// Read all GI from PLC.
		/// </summary>
		/// <param name="component"></param>
		private void Read(SmartComponent component)
		{
			if (client.Connected)
			{
				int giCount = (int)component.Properties["GI_ByteNumber"].Value;
				S7Client.S7DataItem item = new S7Client.S7DataItem();
				if (GetS7DataItem((string)component.Properties["GI_FirstByteAddress"].Value, ref item)
					&& (giCount > 0) )
				{
					byte[] b = new byte[giCount];
					int result = 0x01700000;// S7Consts.errCliInvalidBlockType
					switch (item.Area)
					{
						case 0x82:
							result = client.ABRead(item.Start, giCount, b); //S7Consts.S7AreaPA
							break;
						case 0x83:
							result = client.MBRead(item.Start, giCount, b); //S7Consts.S7AreaMK
							break;
						case 0x84:
							result = client.DBRead(item.DBNumber, item.Start, giCount, b); //S7Consts.S7AreaDB
							break;
					}
					ShowResult(component, result);
					if (result == 0)
					{
						for (int i = 0; i < giCount; ++i)
						{
							string giName = "GI_" + i.ToString();
							if (component.IOSignals.Contains(giName))
							{
								component.IOSignals[giName].Value = (int)b[i];
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Update GI list depends GI_ByteNumber
		/// </summary>
		/// <param name="component">Component that owns signals. </param>
		/// <param name="oldCount">Old GI count</param>
		private void UpdateGICount(SmartComponent component, int oldCount)
		{
			int newGICount = (int)component.Properties["GI_ByteNumber"].Value;
			if (newGICount > oldCount)
			{
				for (int i = oldCount; i < newGICount; i++)
				{
					string giName = "GI_" + i.ToString();
					if (!component.IOSignals.Contains(giName))
					{
						IOSignal ios = new IOSignal(giName, IOSignalType.DigitalGroupOutput);
						ios.ReadOnly = true;
						ios.UIVisible = false;
						component.IOSignals.Add(ios);
					}
				}
			}
			else
			{
				for (int i = oldCount - 1; i >= newGICount; i--)
				{
					string giName = "GI_" + i.ToString();
					if (component.IOSignals.Contains(giName))
					{
						component.IOSignals.Remove(giName);
					}
				}
			}
		}

		/// <summary>
		/// Update GO list depends GO_ByteNumber
		/// </summary>
		/// <param name="component">Component that owns signals. </param>
		/// <param name="oldCount">Old GO count</param>
		private void UpdateGOCount(SmartComponent component, int oldCount)
		{
			int newGOCount = (int)component.Properties["GO_ByteNumber"].Value;
			if (newGOCount > oldCount)
			{
				for (int i = oldCount; i < newGOCount; i++)
				{
					string goName = "GO_" + i.ToString();
					if (!component.IOSignals.Contains(goName))
					{
						IOSignal ios = new IOSignal(goName, IOSignalType.DigitalGroupInput);
						ios.ReadOnly = true;
						ios.UIVisible = false;
						component.IOSignals.Add(ios);
					}
				}
			}
			else
			{
				for (int i = oldCount - 1; i >= newGOCount; i--)
				{
					string goName = "GO_" + i.ToString();
					if (component.IOSignals.Contains(goName))
					{
						component.IOSignals.Remove(goName);
					}
				}
			}
		}

		/// <summary>
		/// This function returns a textual explaination of the error code
		/// </summary>
		/// <param name="component"></param>
		/// <param name="result"></param>
		private void ShowResult(SmartComponent component, int result)
		{
			if (result != 0)
			{
				Disconnect(component);
				component.Properties["Status"].Value = client.ErrorText(result);
				Logger.AddMessage(new LogMessage("WW: " + component.Name + " error: " + client.ErrorText(result), LogMessageSeverity.Warning));
			}
		}

		/// <summary>
		/// Returns a String containing a specified number of characters from the left side of a string.
		/// </summary>
		/// <param name="value">String expression from which the leftmost characters are returned. If string contains Null, Empty is returned.</param>
		/// <param name="length">Numeric expression indicating how many characters to return. If 0, a zero-length string ("") is returned. If greater than or equal to the number of characters in string, the entire string is returned.</param>
		/// <returns></returns>
		private string Left(string value, int length)
		{
			if (string.IsNullOrEmpty(value) || (length <= 0)) return string.Empty;

			return value.Length <= length ? value : value.Substring(0, length);
		}

		/// <summary>
		/// Returns a String containing a specified number of characters from the right side of a string.
		/// </summary>
		/// <param name="value">String expression from which the rightmost characters are returned. If string contains Null, Empty is returned.</param>
		/// <param name="length">Numeric expression indicating how many characters to return. If 0, a zero-length string ("") is returned. If greater than or equal to the number of characters in string, the entire string is returned.</param>
		/// <returns></returns>
		private string Right(string value, int length)
		{
			if (string.IsNullOrEmpty(value) || (length <= 0)) return string.Empty;

			return value.Length <= length ? value : value.Substring(value.Length - length);
		}

		/// <summary>
		/// Returns a Boolean value indicating whether an expression can be evaluated as an integer.
		/// </summary>
		/// <param name="value">String expression containing an integer expression or string expression.</param>
		/// <returns>IsInteger returns True if the entire expression is recognized as an integer; otherwise, it returns False.</returns>
		private bool IsInteger(string value)
		{
			int output;
			return int.TryParse(value, out output);
		}

		/// <summary>
		/// Populate S7DataItem struct depends name (ex: MB500).
		/// </summary>
		/// <param name="name">Name of data.</param>
		/// <param name="item">Struct to be populated.</param>
		/// <returns>True if name is in good syntax.</returns>
		private bool GetS7DataItem(string name, ref S7Client.S7DataItem item)
		{
			string strName = name.ToUpper();
			if (string.IsNullOrEmpty(strName))
				return false;

			if (strName.Substring(0, 1) == "M")
			{
				item.Area = S7Consts.S7AreaMK;
				if (strName.Length < 2) //Mx0 || M0.
					return false;
				item.WordLen = GetWordLength(strName.Substring(1, 1));
				string strOffset = Right(strName, strName.Length - (item.WordLen == S7Consts.S7WLBit ? 1 : 2));
				int offset;
				if (!int.TryParse(strOffset, out offset))
					return false;
				item.Start = offset;
			}
			else if (strName.Substring(0, 1) == "A")
			{
				item.Area = S7Consts.S7AreaPA;
				if (strName.Length < 2) //Ax0 || A0.
					return false;
				item.WordLen = GetWordLength(strName.Substring(1, 1));
				string strOffset = Right(strName, strName.Length - (item.WordLen == S7Consts.S7WLBit ? 1 : 2));
				int offset;
				if (!int.TryParse(strOffset, out offset))
					return false;
				item.Start = offset;
			}
			else if (strName.Substring(0, 1) == "E")
			{
				item.Area = S7Consts.S7AreaPE;
				if (strName.Length < 2) //Ex0 || E0.
					return false;
				item.WordLen = GetWordLength(strName.Substring(1, 1));
				string strOffset = Right(strName, strName.Length - (item.WordLen == S7Consts.S7WLBit ? 1 : 2));
				int offset;
				if (!int.TryParse(strOffset, out offset))
					return false;
				item.Start = offset;
			}
			else if ((strName.Length >= 2) && (strName.Substring(0, 2) == "DB"))
			{
				item.Area = S7Consts.S7AreaDB;
				if (strName.Length < 3) //DB0
					return false;
				string strDBNumber = Right(strName, strName.Length - 2);
				strDBNumber = Left(strDBNumber, strDBNumber.IndexOf(".") == -1 ? strDBNumber.Length : strDBNumber.IndexOf("."));
				int dbNumber;
				if (!int.TryParse(strDBNumber, out dbNumber))
					return false;
				item.DBNumber = dbNumber;
				int index = strName.IndexOf(".DB");
				if ((index < 0) || (strName.Length < (index + 4))) //.DBx
					return false;
				item.WordLen = GetWordLength(strName.Substring(index + 3, 1));
				string strOffset = Right(strName, strName.Length - index - 4); //.DBx = 4
				int offset;
				if (!int.TryParse(strOffset, out offset))
					return false;
				item.Start = offset;
			}
			else
				return false;

			return true;
		}

		/// <summary>
		/// Return WordLength depends char
		/// </summary>
		/// <param name="word">Char to design type.</param>
		/// <returns>By default returns S7WLBit.</returns>
		private int GetWordLength(string word)
		{
			if (word.ToUpper() == "X")
				return S7Consts.S7WLBit;
			if (word.ToUpper() == "B")
				return S7Consts.S7WLByte;
			else if (word.ToUpper() == "W")
				return S7Consts.S7WLWord;
			else if (word.ToUpper() == "D")
				return S7Consts.S7WLDWord;
			return S7Consts.S7WLBit;
		}
	}
}
