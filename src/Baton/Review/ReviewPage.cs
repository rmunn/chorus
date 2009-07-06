﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Baton.HistoryPanel.ChangedRecordControl;
using Baton.Review.RevisionChanges;

namespace Baton.HistoryPanel
{
	public partial class ReviewPage : UserControl
	{

		 public ReviewPage(HistoryPanel historyPanel, RevisionChangesView revisionChangesView, ChangedRecordView changedRecordView)
		{
			InitializeComponent();
			SuspendLayout();
			var lowerContainer = new SplitContainer();
			lowerContainer.Orientation = Orientation.Vertical;
			 lowerContainer.Dock = DockStyle.Fill;
			 revisionChangesView.Dock = DockStyle.Fill;
			 changedRecordView.Dock = DockStyle.Fill;
			lowerContainer.Panel1.Controls.Add(revisionChangesView);
			lowerContainer.Panel2.Controls.Add(changedRecordView);

			var verticalContainer = new SplitContainer();
			 verticalContainer.Orientation = Orientation.Horizontal;
			 historyPanel.Dock = DockStyle.Fill;
			verticalContainer.Panel1.Controls.Add(historyPanel);
			verticalContainer.Panel2.Controls.Add(lowerContainer);
			 verticalContainer.Dock = DockStyle.Fill;
			 Controls.Add(verticalContainer);
			ResumeLayout();
		}
	}
}
