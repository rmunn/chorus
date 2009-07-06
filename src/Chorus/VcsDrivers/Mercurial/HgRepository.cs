using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Chorus.retrieval;
using Chorus.sync;
using Chorus.Utilities;
using Chorus.merge;

namespace Chorus.VcsDrivers.Mercurial
{

	public class HgRepository : IRetrieveFile
	{
		protected readonly string _pathToRepository;
		protected readonly string _userName;
		protected IProgress _progress;

		public static string GetEnvironmentReadinessMessage(string messageLanguageId)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo();
			startInfo.FileName = "hg";
			startInfo.Arguments = "version";
			startInfo.CreateNoWindow = true;
			startInfo.UseShellExecute = false;
			try
			{
				System.Diagnostics.Process.Start(startInfo);
			}
			catch(Exception error)
			{
				 return "Sorry, this feature requires the Mercurial version control system.  It must be installed and part of the PATH environment variable.  Windows users can download and install TortoiseHg";
			}
			return null;
		}

		protected Revision GetMyHead()
		{
			using (new ConsoleProgress("Getting real head of {0}", _userName))
			{
				string result = GetTextFromQuery(_pathToRepository, "identify -nib");
				string[] parts = result.Split(new char[] {' ','(',')'}, StringSplitOptions.RemoveEmptyEntries);
				Revision descriptor = new Revision(this, parts[2],parts[1], parts[0], "unknown");

				return descriptor;
			}
		}


		public HgRepository(string pathToRepository, IProgress progress)
		{
			_pathToRepository = pathToRepository;
			_progress = progress;
			_userName = GetUserIdInUse();
		}

		static protected void SetupPerson(string pathToRepository, string userName)
		{
			using (new ConsoleProgress("setting name and branch"))
			{
				using (new ShortTermEnvironmentalVariable("HGUSER", userName))
				{
					Execute("branch", pathToRepository, userName);
				}
			}
		}

		public void TryToPull(string resolvedUri)
		{
			HgRepository repo = new HgRepository(resolvedUri, _progress);
			PullFromRepository(repo, false);
		}

		public void Push(string targetUri, IProgress progress, SyncResults results)
		{
			using (new ConsoleProgress("{0} pushing to {1}", _userName, targetUri))
			{
				try
				{
					Execute("push", _pathToRepository, SurroundWithQuotes(targetUri));
				}
				catch (Exception err)
				{
					_progress.WriteWarning("Could not push to " + targetUri + Environment.NewLine + err.Message);
				}
				try
				{
					Execute("update", targetUri); // for usb keys and other local repositories
				}
				catch (Exception err)
				{
					_progress.WriteWarning("Could not update the actual files after a pull at " + targetUri + Environment.NewLine + err.Message);
				}
			}
		}

		protected void PullFromRepository(HgRepository otherRepo,bool throwIfCannot)
		{
			using (new ConsoleProgress("{0} pulling from {1}", _userName,otherRepo.Name))
			{
				try
				{
					Execute("pull", _pathToRepository, otherRepo.PathWithQuotes);
				}
				catch (Exception err)
				{
					if (throwIfCannot)
					{
						throw err;
					}
					_progress.WriteWarning("Could not pull from " + otherRepo.Name);
				}
			}
		}


		private List<Revision> GetBranches()
		{
			string what= "branches";
			using (new ConsoleProgress("Getting {0} of {1}", what, _userName))
			{
				string result = GetTextFromQuery(_pathToRepository, what);

				string[] lines = result.Split('\n');
				List<Revision> branches = new List<Revision>();
				foreach (string line in lines)
				{
					if (line.Trim() == "")
						continue;

					string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length < 2)
						continue;
					string[] revisionParts = parts[1].Split(':');
					branches.Add(new Revision(this, parts[0], revisionParts[0], revisionParts[1], "unknown"));
				}
				return branches;
			}
		}

		private Revision GetTip()
		{
			return GetRevisionsFromQuery("tip")[0];
		}

		protected List<Revision> GetHeads()
		{
			using (new ConsoleProgress("Getting heads of {0}", _userName))
			{
				return GetRevisionsFromQuery("heads");
			}
		}


		protected static string GetTextFromQuery(string repositoryPath, string s)
		{
			ExecutionResult result= ExecuteErrorsOk(s + " -R " + SurroundWithQuotes(repositoryPath));
			Debug.Assert(string.IsNullOrEmpty(result.StandardError), result.StandardError);
			return result.StandardOutput;
		}
		protected static string GetTextFromQuery(string s)
		{
			ExecutionResult result = ExecuteErrorsOk(s);
			//TODO: we need a way to get this kind of error back the devs for debugging
			Debug.Assert(string.IsNullOrEmpty(result.StandardError), result.StandardError);
			return result.StandardOutput;
		}

		public void AddAndCheckinFile(string filePath)
		{
			TrackFile(filePath);
			Commit(false, " Add " + Path.GetFileName(filePath));
		}

		private void TrackFile(string filePath)
		{
			using (new ConsoleProgress("Adding {0} to the files that are tracked for {1}: ", Path.GetFileName(filePath), _userName))
			{
				Execute("add", _pathToRepository, SurroundWithQuotes(filePath));
			}
		}

		public virtual void Commit(bool forceCreationOfChangeSet, string message, params object[] args)
		{
			//enhance: this is normally going to be redundant, as we always use the same branch.
			//but it does it set the first time, and handles the case where the user's account changes (either
			//because they've logged in as a different user, or changed the name of a their account.

			//NB: I (JH) and not yet even clear we need branches, and it makes reading the tree somewhat confusing
			//If Bob merges with Sally, his new "tip" can very well be labelled "Sally".

			//disabled because then Update failed to get the latest, if it was the other user's branch
			//      Branch(_userName);

			message = string.Format(message, args);
			using (new ConsoleProgress("{0} committing with comment: {1}", _userName, message))
			{
				ExecutionResult result = Execute("ci", _pathToRepository, "-m " + SurroundWithQuotes(message));
				_progress.WriteMessage(result.StandardOutput);
			}
		}




		public void Branch(string branchName)
		{
			using (new ConsoleProgress("{0} changing working dir to branch: {1}", _userName, branchName))
			{
				Execute("branch -f ", _pathToRepository, SurroundWithQuotes(branchName));
			}
		}

		protected static ExecutionResult Execute(string cmd, string repositoryPath, params string[] rest)
		{
			return Execute(false, cmd, repositoryPath, rest);
		}
		protected static ExecutionResult Execute(bool failureIsOk, string cmd, string repositoryPath, params string[] rest)
		{
			StringBuilder b = new StringBuilder();
			b.Append(cmd + " ");
			if (!string.IsNullOrEmpty(repositoryPath))
			{
				b.Append("-R " + SurroundWithQuotes(repositoryPath) + " ");
			}
			foreach (string s in rest)
			{
				b.Append(s + " ");
			}

			ExecutionResult result = ExecuteErrorsOk(b.ToString());
			if (0 != result.ExitCode && !failureIsOk)
			{
				var details = "\r\n" + "hg Command was " + "\r\n" +  b.ToString();
				try
				{
					details += "\r\nhg version was \r\n" + GetTextFromQuery("version");
				}
				catch (Exception)
				{
					details += "\r\nCould not get HG VERSION";

				}

				if (!string.IsNullOrEmpty(result.StandardError))
				{
					throw new ApplicationException(result.StandardError + details);
				}
				else
				{
					throw new ApplicationException("Got return value " + result.ExitCode + details);
				}
			}
			return result;
		}

		protected static ExecutionResult ExecuteErrorsOk(string command, string fromDirectory)
		{
			//    _progress.WriteMessage("hg "+command);

			return HgRunner.Run("hg " + command, fromDirectory);
		}

		protected static ExecutionResult ExecuteErrorsOk(string command)
		{
			return ExecuteErrorsOk(command, null);
		}


		protected static string SurroundWithQuotes(string path)
		{
			return "\"" + path + "\"";
		}

		public string PathWithQuotes
		{
			get
			{
				return "\"" + _pathToRepository + "\"";
			}
		}

		public string PathToRepo
		{
			get { return _pathToRepository; }
		}

		public string UserName
		{
			get { return _userName; }
		}

		private string Name
		{
			get { return _userName; } //enhance... location is important, too
		}

		public string GetFilePath(string name)
		{
			return Path.Combine(_pathToRepository, name);
		}

		public List<string> GetChangedFiles()
		{
			ExecutionResult result= Execute("status", _pathToRepository);
			string[] lines = result.StandardOutput.Split('\n');
			List<string> files = new List<string>();
			foreach (string line in lines)
			{
				if(line.Trim()!="")
					files.Add(line.Substring(2)); //! data.txt
			}

			return files;
		}

		public void Update()
		{
			using (new ConsoleProgress("{0} updating",_userName))
			{
				Execute("update", _pathToRepository);
			}
		}

		public void Update(string revision)
		{
			using (new ConsoleProgress("{0} updating (making working directory contain) revision {1}", _userName, revision))
			{
				Execute("update", _pathToRepository, "-r", revision, "-C");
			}
		}

//        public void GetRevisionOfFile(string fileRelativePath, string revision, string fullOutputPath)
//        {
//            //for "hg cat" (surprisingly), the relative path isn't relative to the start of the repo, but to the current
//            // directory.
//            string absolutePathToFile = SurroundWithQuotes(Path.Combine(_pathToRepository, fileRelativePath));
//
//            Execute("cat", _pathToRepository, "-o ",fullOutputPath," -r ",revision,absolutePathToFile);
//        }

		public static void CreateRepositoryInExistingDir(string path)
		{
			Execute("init", null, SurroundWithQuotes(path));
		}


		/// <summary>
		/// note: intentionally does not commit afterwards
		/// </summary>
		/// <param name="progress"></param>
		/// <param name="results"></param>
		public IList<string> MergeHeads(IProgress progress, SyncResults results)
		{
			List<string> peopleWeMergedWith= new List<string>();
			Revision rev= GetMyHead();

			List<Revision> heads = GetHeads();
			Revision myHead = GetMyHead();
			foreach (Revision theirHead in heads)
			{
				MergeSituation.PushRevisionsToEnvironmentVariables(myHead.LocalRevisionNumber, theirHead.LocalRevisionNumber);

				MergeOrder.PushToEnvironmentVariables(_pathToRepository);
				if (theirHead.LocalRevisionNumber != myHead.LocalRevisionNumber)
				{
					bool didMerge = MergeTwoChangeSets(myHead, theirHead);
					if (didMerge)
					{
						peopleWeMergedWith.Add(theirHead.UserId);
					}
				}
			}

			return peopleWeMergedWith;
		}

		private bool MergeTwoChangeSets(Revision head, Revision theirHead)
		{
			ExecutionResult result = null;
			using (new ShortTermEnvironmentalVariable("HGMERGE", Path.Combine(Other.DirectoryOfExecutingAssembly, "ChorusMerge.exe")))
			{
				using (new ShortTermEnvironmentalVariable(MergeOrder.kConflictHandlingModeEnvVarName, MergeOrder.ConflictHandlingModeChoices.TheyWin.ToString()))

				{
					result = Execute(true, "merge", _pathToRepository, "-r", theirHead.LocalRevisionNumber);
				}
			}
			if (result.ExitCode != 0)
			{
				if (result.StandardError.Contains("nothing to merge"))
				{
//                    _progress.WriteMessage("Nothing to merge, updating instead to revision {0}.", theirChangeSet._revision);
//                    Update(theirChangeSet._revision);//REVIEW
					return false;
				}
				else
				{
					throw new ApplicationException(result.StandardError);
				}
			}
			return true;
		}

		public void AddAndCheckinFiles(List<string> includePatterns, List<string> excludePatterns, string message)
		{
			StringBuilder args = new StringBuilder();
			foreach (string pattern in includePatterns)
			{
				string p = Path.Combine(this._pathToRepository, pattern);
				args.Append(" -I " + SurroundWithQuotes(p));
			}

			args.Append(" -I " + SurroundWithQuotes(Path.Combine(this._pathToRepository, "**.conflicts")));
			args.Append(" -I " + SurroundWithQuotes(Path.Combine(this._pathToRepository, "**.conflicts.txt")));

			foreach (string pattern in excludePatterns)
			{
				//this fails:   hg add -R "E:\Users\John\AppData\Local\Temp\ChorusTest"  -X "**/cache"
				//but this works  -X "E:\Users\John\AppData\Local\Temp\ChorusTest/**/cache"
				string p = Path.Combine(this._pathToRepository, pattern);
				args.Append(" -X " + SurroundWithQuotes(p));
			}

			//enhance: what happens if something is covered by the exclusion pattern that was previously added?  Will the old
			// version just be stuck on the head?

			if (GetIsAtLeastOneMissingFileInWorkingDir())
			{
				using (new ConsoleProgress("At least one file was removed from the working directory.  Telling Hg to record the deletion."))
				{
					Execute("rm -A", _pathToRepository);
				}
			}
			using (new ConsoleProgress("Adding files to be tracked ({0}", args.ToString()))
			{
				Execute("add", _pathToRepository, args.ToString());
			}
			using (new ConsoleProgress("Committing \"{0}\"", message))
			{
				Commit(false, message);
			}
		}

		public static string GetRepositoryRoot(string directoryPath)
		{
//            string old = Directory.GetCurrentDirectory();
//            try
//            {
			// Directory.SetCurrentDirectory(directoryPath);
			ExecutionResult result = ExecuteErrorsOk("root", directoryPath);
			if (result.ExitCode == 0)
			{
				return result.StandardOutput.Trim();
			}
			return null;
//            }
//            finally
//            {
//                Directory.SetCurrentDirectory(old);
//            }
		}


		private void PrintHeads(List<Revision> heads, Revision myHead)
		{
			_progress.WriteMessage("Current Heads:");
			foreach (Revision head in heads)
			{
				if (head.LocalRevisionNumber == myHead.LocalRevisionNumber)
				{
					_progress.WriteMessage("  ME {0} {1} {2}", head.UserId, head.LocalRevisionNumber, head.Summary);
				}
				else
				{
					_progress.WriteMessage("      {0} {1} {2}", head.UserId, head.LocalRevisionNumber, head.Summary);
				}
			}
		}

		public void Clone(string path)
		{
			Execute("clone", null, PathWithQuotes + " " + SurroundWithQuotes(path));
		}

		private List<Revision> GetRevisionsFromQuery(string query)
		{
			string result = GetTextFromQuery(_pathToRepository, query);
			return GetRevisionsFromQueryResultText(result);
		}

 /*       private static List<Revision> GetRevisionsFromQueryOutput(string result)
		{
			//Debug.WriteLine(result);
			string[] lines = result.Split('\n');
			List<Dictionary<string, string>> rawChangeSets = new List<Dictionary<string, string>>();
			Dictionary<string, string> rawChangeSet = null;
			foreach (string line in lines)
			{
				if (line.StartsWith("changeset:"))
				{
					rawChangeSet = new Dictionary<string, string>();
					rawChangeSets.Add(rawChangeSet);
				}
				string[] parts = line.Split(new char[] { ':' });
				if (parts.Length < 2)
					continue;
				//join all but the first back together
				string contents = string.Join(":", parts, 1, parts.Length - 1);
				rawChangeSet[parts[0].Trim()] = contents.Trim();
			}

			List<Revision> revisions = new List<Revision>();
			foreach (Dictionary<string, string> d in rawChangeSets)
			{
				string[] revisionParts = d["changeset"].Split(':');
				string summary = string.Empty;
				if (d.ContainsKey("summary"))
				{
					summary = d["summary"];
				}
				Revision revision = new Revision(d["user"], revisionParts[0], /*revisionParts[1]/"unknown", summary);
				if (d.ContainsKey("tag"))
				{
					revision.Tag = d["tag"];
				}
				revisions.Add(revision);

			}
			return revisions;
		}
*/
		public List<Revision> GetAllRevisions()
		{
			/*
				changeset:   0:7ee3570760cd
				tag:         tip
				user:        hattonjohn@gmail.com
				date:        Wed Jul 02 16:40:26 2008 -0600
				summary:     bob: first one
			 */

			string result = GetTextFromQuery(_pathToRepository, "log");
			return GetRevisionsFromQueryResultText(result);
		}

		public List<Revision> GetRevisionsFromQueryResultText(string queryResultText)
		{
			TextReader reader = new StringReader(queryResultText);
			string line = reader.ReadLine();


			List<Revision> items = new List<Revision>();
			Revision item = null;
			while(line !=null)
			{
				int colonIndex = line.IndexOf(":");
				if(colonIndex >0 )
				{
					string label = line.Substring(0, colonIndex);
					string value = line.Substring(colonIndex + 1).Trim();
					switch (label)
					{
						default:
							break;
						case "changeset":
							item = new Revision(this);
							items.Add(item);
							item.SetRevisionAndHashFromCombinedDescriptor(value);
							break;

						case "user":
							item.UserId = value;
							break;

						case "date":
							item.DateString = value;
							break;

						case "summary":
							item.Summary = value;
							break;

						case "tag":
							item.Tag = value;
							break;
					}
				}
				line = reader.ReadLine();
			}
			return items;
		}

		public static void SetUserId(string path, string userId)
		{
		  Environment.SetEnvironmentVariable("hguser", userId);
		  //defunct Execute("config", path, "--local ui.username " + userId);

		}

		public string GetUserIdInUse()
		{
			return GetTextFromQuery(_pathToRepository, "showconfig ui.username").Trim();
		}

		public bool GetFileExistsInRepo(string subPath)
		{
			string result = GetTextFromQuery(_pathToRepository, "locate " + subPath);
			return !String.IsNullOrEmpty(result.Trim());
		}
		public bool GetIsAtLeastOneMissingFileInWorkingDir()
		{
			string result = GetTextFromQuery(_pathToRepository, "status -d ");
			return !String.IsNullOrEmpty(result.Trim());
		}

		/// <summary>
		///  From IRetrieveFile
		/// </summary>
		/// <returns>path to a temp file. caller is responsible for deleting the file.</returns>
		public string RetrieveHistoricalVersionOfFile(string relativePath, string revOrHash)
		{
			Guard.Against(string.IsNullOrEmpty(revOrHash), "The revision cannot be empty (note: the first revision has an empty string for its parent revision");
			var f =  TempFile.CreateWithExtension(Path.GetExtension(relativePath));

			var cmd = string.Format("cat -o \"{0}\" -r {1} \"{2}\"", f.Path, revOrHash, relativePath);
			ExecutionResult result = ExecuteErrorsOk(cmd, _pathToRepository);
			if(!string.IsNullOrEmpty(result.StandardError.Trim()))
			{
				throw new ApplicationException(String.Format("Could not retrieve version {0} of {1}. Mercurial said: {2}", revOrHash, relativePath, result.StandardError));
			}
			return f.Path;
		}

		public IEnumerable<FileInRevision> GetFilesInRevision(Revision revision)
		{
			var query =  "status --rev "+GetRevisionRangeForSingleRevisionDiff(revision);
			var result = GetTextFromQuery(_pathToRepository,query);
			string[] lines = result.Split('\n');
			var revisions = new List<FileInRevision>();
			foreach (string line in lines)
			{
				if (line.Trim() == "")
					continue;
				var actionLetter = line[0];
				var action = ParseActionLetter(actionLetter);

				//if this is the first rev in the whole repo, then the only way to list the fils
				//is to include the "clean" ones.  Better to represent that as an Add
				if (action == FileInRevision.Action.NoChanges)
					action = FileInRevision.Action.Added;

				revisions.Add(new FileInRevision(line.Substring(2), action));
			}

			return revisions;
		}

		private string GetRevisionRangeForSingleRevisionDiff(Revision revision)
		{
			var parent = GetLocalNumberForParentOfRevision(revision.LocalRevisionNumber);
			if (parent == string.Empty)
				return string.Format("{0} -A", revision.LocalRevisionNumber);
			else
				return string.Format("{0}:{1}", parent, revision.LocalRevisionNumber);
		}

		public string GetLocalNumberForParentOfRevision(string localRevisionNumber)
		{

			var result = GetTextFromQuery(_pathToRepository,"hg parent -y -r " + localRevisionNumber + " --template {rev}");
			return result.Trim();
		}

		private static FileInRevision.Action ParseActionLetter(char actionLetter)
		{
		   switch (actionLetter)
				{
					case 'A':
						return FileInRevision.Action.Added;
						break;
					case 'M':
						return FileInRevision.Action.Modified;
						break;
					case 'D':
						return FileInRevision.Action.Deleted;
						break;
					case 'C':
						return FileInRevision.Action.NoChanges;
						break;
					default:
						return FileInRevision.Action.Unknown;
				}
		}
	}

	public class FileInRevision
	{
		private readonly Action _action;

		public enum Action
		{
			Added, Deleted, Modified,
			Unknown,
			NoChanges
		}
		public string RelativePath { get; private set; }
		public FileInRevision(string relativePath, Action action)
		{
			_action = action;
			RelativePath = relativePath;
		}
	}
}