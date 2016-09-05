﻿using System;
using System.Reflection;

namespace EditorFormulas 
{
	[System.Serializable]
	public class FormulaData
	{
		//Should match class name
		public string name;
		public string niceName;
		public string tooltip;
		public string author;
		public string downloadURL;
		public string htmlURL;
		public string apiURL;
		public string projectFilePath;
		public bool IsUsable
		{
			get { return methodInfo != null; }
		}
			
		public long downloadTimeUTCBinary;
		public DateTime DownloadTimeUTC
		{
			get
			{
				return DateTime.FromBinary(downloadTimeUTCBinary);
			}
			set
			{
				downloadTimeUTCBinary = value.ToBinary();
			}
		}

		public bool updateAvailable = false;

		public long updateCheckTimeUTCBinary;
		public DateTime UpdateCheckTimeUTC
		{
			get
			{
				return DateTime.FromBinary(updateCheckTimeUTCBinary);
			}
			set
			{
				updateCheckTimeUTCBinary = value.ToBinary();
			}
		}

		public bool hidden = false;

		[System.NonSerialized]
		public MethodInfo methodInfo;
		[System.NonSerialized]
		public bool localFileExists;
	}
}