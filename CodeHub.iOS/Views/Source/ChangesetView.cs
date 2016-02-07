using System;
using CodeHub.iOS.ViewControllers;
using MonoTouch.Dialog;
using UIKit;
using CodeFramework.iOS.Utils;
using System.Linq;
using Foundation;
using CodeHub.Core.ViewModels.Changesets;
using CodeHub.iOS.Elements;
using Humanizer;

namespace CodeHub.iOS.Views.Source
{
    public class ChangesetView : PrettyDialogViewController
    {
        public new ChangesetViewModel ViewModel 
        {
            get { return (ChangesetViewModel)base.ViewModel; }
			set { base.ViewModel = value; }
        }
        
        public ChangesetView()
        {
			NavigationItem.RightBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Action, (s, e) => ShowExtraMenu());
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            Title = "Commit " + (ViewModel.Node.Length > 6 ? ViewModel.Node.Substring(0, 6) : ViewModel.Node);

            HeaderView.SetImage(null, Images.Avatar);
            HeaderView.Text = Title;
            TableView.RowHeight = UITableView.AutomaticDimension;
            TableView.EstimatedRowHeight = 44f;

            ViewModel.Bind(x => x.Changeset, x => {
                var msg = x.Commit.Message ?? string.Empty;
                msg = msg.Split('\n')[0];
                HeaderView.Text = msg.Split('\n')[0];
                HeaderView.SubText = "Commited " + (ViewModel.Changeset?.Commit?.Committer?.Date ?? DateTimeOffset.Now).Humanize();
                HeaderView.SetImage(x.Author?.AvatarUrl, Images.Avatar);
                RefreshHeaderView();
            });

            ViewModel.Bind(x => x.Changeset, Render);
            ViewModel.BindCollection(x => x.Comments, a => Render());
        }

        public void Render()
        {
            var commitModel = ViewModel.Changeset;
            if (commitModel == null)
                return;

            var root = new RootElement(Title);

            var additions = ViewModel.Changeset.Stats?.Additions ?? 0;
            var deletions = ViewModel.Changeset.Stats?.Deletions ?? 0;

            var split = new SplitButtonElement();
            split.AddButton("Additions", additions.ToString());
            split.AddButton("Deletions", deletions.ToString());
            split.AddButton("Parents", ViewModel.Changeset.Parents.Count().ToString());

            var headerSection = new Section() { split };
            root.Add(headerSection);

            var detailSection = new Section();
            root.Add(detailSection);

            var user = "Unknown";
            if (commitModel.Commit.Author != null)
                user = commitModel.Commit.Author.Name;
            if (commitModel.Commit.Committer != null)
                user = commitModel.Commit.Committer.Name;

            detailSection.Add(new MultilinedElement(user, commitModel.Commit.Message)
            {
                CaptionColor = Theme.CurrentTheme.MainTextColor,
                ValueColor = Theme.CurrentTheme.MainTextColor,
                BackgroundColor = UIColor.White
            });

            if (ViewModel.ShowRepository)
            {
                var repo = new StyledStringElement(ViewModel.Repository) { 
                    Accessory = UIKit.UITableViewCellAccessory.DisclosureIndicator, 
                    Lines = 1, 
                    Font = StyledStringElement.DefaultDetailFont, 
                    TextColor = StyledStringElement.DefaultDetailColor,
                    Image = Octicon.Repo.ToImage()
                };
                repo.Tapped += () => ViewModel.GoToRepositoryCommand.Execute(null);
                detailSection.Add(repo);
            }

			var paths = commitModel.Files.GroupBy(y => {
				var filename = "/" + y.Filename;
				return filename.Substring(0, filename.LastIndexOf("/", System.StringComparison.Ordinal) + 1);
			}).OrderBy(y => y.Key);

			foreach (var p in paths)
			{
				var fileSection = new Section(p.Key);
				foreach (var x in p)
				{
					var y = x;
					var file = x.Filename.Substring(x.Filename.LastIndexOf('/') + 1);
					var sse = new ChangesetElement(file, x.Status, x.Additions, x.Deletions);
					sse.Tapped += () => ViewModel.GoToFileCommand.Execute(y);
					fileSection.Add(sse);
				}
				root.Add(fileSection);
			}
//
//			var fileSection = new Section();
//            commitModel.Files.ForEach(x => {
//                var file = x.Filename.Substring(x.Filename.LastIndexOf('/') + 1);
//                var sse = new ChangesetElement(file, x.Status, x.Additions, x.Deletions);
//                sse.Tapped += () => ViewModel.GoToFileCommand.Execute(x);
//                fileSection.Add(sse);
//            });

//            if (fileSection.Elements.Count > 0)
//                root.Add(fileSection);
//

			var commentSection = new Section();
            foreach (var comment in ViewModel.Comments)
            {
                //The path should be empty to indicate it's a comment on the entire commit, not a specific file
                if (!string.IsNullOrEmpty(comment.Path))
                    continue;

                commentSection.Add(new CommentElement(comment.User.Login, comment.Body, comment.CreatedAt, comment.User.AvatarUrl));
            }

			if (commentSection.Elements.Count > 0)
				root.Add(commentSection);

            var addComment = new StyledStringElement("Add Comment".t()) { Image = Octicon.Pencil.ToImage() };
            addComment.Tapped += AddCommentTapped;
			root.Add(new Section { addComment });
            Root = root; 
        }

        void AddCommentTapped()
        {
            var composer = new MarkdownComposerViewController();
			composer.NewComment(this, async (text) => {
                try
                {
					await composer.DoWorkAsync("Commenting...".t(), () => ViewModel.AddComment(text));
					composer.CloseComposer();
                }
                catch (Exception e)
                {
					MonoTouch.Utilities.ShowAlert("Unable to post comment!", e.Message);
                }
                finally
                {
                    composer.EnableSendButton = true;
                }
            });
        }

		private void ShowExtraMenu()
		{
			var changeset = ViewModel.Changeset;
			if (changeset == null)
				return;

            var sheet = new UIActionSheet();
			var addComment = sheet.AddButton("Add Comment".t());
			var copySha = sheet.AddButton("Copy Sha".t());
			var shareButton = sheet.AddButton("Share".t());
			//var showButton = sheet.AddButton("Show in GitHub".t());
			var cancelButton = sheet.AddButton("Cancel".t());
			sheet.CancelButtonIndex = cancelButton;
			sheet.DismissWithClickedButtonIndex(cancelButton, true);
            sheet.Dismissed += (s, e) =>
            {
                BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        // Pin to menu
                        if (e.ButtonIndex == addComment)
                        {
                            AddCommentTapped();
                        }
                        else if (e.ButtonIndex == copySha)
                        {
                            UIPasteboard.General.String = ViewModel.Changeset.Sha;
                        }
                        else if (e.ButtonIndex == shareButton)
                        {
                            var item = new NSUrl(ViewModel.Changeset.Url);
                            var activityItems = new Foundation.NSObject[] { item };
                            UIActivity[] applicationActivities = null;
                            var activityController = new UIActivityViewController(activityItems, applicationActivities);
                            PresentViewController(activityController, true, null);
                        }
                        //				else if (e.ButtonIndex == showButton)
                        //				{
                        //					ViewModel.GoToHtmlUrlCommand.Execute(null);
                        //				}
                    }
                    catch
                    {
                    }
                });

                sheet.Dispose();
            };

			sheet.ShowInView(this.View);
		}
    }
}

