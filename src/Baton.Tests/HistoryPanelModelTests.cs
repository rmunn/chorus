﻿using System.Collections.Generic;
using System.IO;
using Baton.HistoryPanel;
using Chorus.sync;
using Chorus.Utilities;
using Chorus.VcsDrivers.Mercurial;
using NUnit.Framework;

namespace Baton.Tests
{
	[TestFixture]
	public class HistoryPanelModelTests
	{
		private string _pathToTestRoot;
		private HistoryPanelModel _model;
		private StringBuilderProgress _progress;
		private ProjectFolderConfiguration _project;

		//Not much to test here yet, as the history-getting itself is tested at a lower level

		[SetUp]
		public void Setup()
		{
			_pathToTestRoot = Path.Combine(Path.GetTempPath(), "ChorusTest");
			if (Directory.Exists(_pathToTestRoot))
				Directory.Delete(_pathToTestRoot, true);
			Directory.CreateDirectory(_pathToTestRoot);


			string pathToText = Path.Combine(_pathToTestRoot, "foo.txt");
			File.WriteAllText(pathToText, "version one of my pretend txt");

			RepositoryManager.MakeRepositoryForTest(_pathToTestRoot, "bob");

			_project = new ProjectFolderConfiguration(_pathToTestRoot);
			_project.FolderPath = _pathToTestRoot;
			_project.IncludePatterns.Add(pathToText);
			_project.FolderPath = _pathToTestRoot;

			_progress = new StringBuilderProgress();
			_model = new HistoryPanelModel(RepositoryManager.FromRootOrChildFolder(_project), null);
			_model.ProgressDisplay = _progress;
		}

		[Test]
		public void BeforeAnySyncing_EmptyHistory()
		{
			List<Revision> items = _model.GetHistoryItems();
			Assert.AreEqual(0, items.Count);
		}
	}
}