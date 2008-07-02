﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Media;
using System.Text;
using Chorus.sync;
using Chorus.Utilities;

namespace Chorus.UI
{
	internal class SyncPanelModel
	{
		private readonly ProjectFolderConfiguration _project;
		private readonly IProgress _progress;
		public List<RepositorySource> RepositoriesToTry = new List<RepositorySource>();
		public IList<RepositorySource> RepositoriesToList;

		public  SyncPanelModel(ProjectFolderConfiguration project, string userName, IProgress progress)
		{
			_project = project;
			_progress = progress;

			RepositoryManager manager = RepositoryManager.FromContext(_project);
			RepositoriesToList= manager.KnownRepositories;
			RepositoriesToTry.AddRange(RepositoriesToList);
		}

		public bool EnableSync
		{
			get {
				return true; //because "checking in" locally is still worth doing
				//return RepositoriesToTry.Count > 0;
			}
		}

		public void Sync()
		{
			RepositoryManager manager = RepositoryManager.FromContext(_project);

			SyncOptions options = new SyncOptions();
			options.DoPullFromOthers = true;
			options.DoMergeWithOthers = true;
			options.RepositoriesToTry = RepositoriesToTry;

			manager.SyncNow(options, _progress);
			SoundPlayer player = new SoundPlayer(@"C:\chorus\src\sounds\finished.wav");
			player.Play();
		}

	}
}
