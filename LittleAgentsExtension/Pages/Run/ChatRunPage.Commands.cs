using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LittleAgentsExtension;

internal sealed partial class ChatRunPage
{
    private void InitializeCommands()
    {
        Commands =
        [
            new CommandContextItem(new CopyResultCommand(this)) { Title = "Copy result" },
            new CommandContextItem(new CopyTranscriptCommand(this)) { Title = "Copy transcript" },
            new CommandContextItem(new StopCommand(this)) { Title = "Stop" },
            new CommandContextItem(new RerunCommand(this)) { Title = "Re-run" },
            new CommandContextItem(new ReplyCommand(this)) { Title = "Reply" },
        ];
    }

    private sealed partial class CopyResultCommand : InvokableCommand
    {
        private readonly ChatRunPage _page;

        public CopyResultCommand(ChatRunPage page)
        {
            _page = page;
            Id = "little-agents.run.copy-result";
            Name = "Copy result";
        }

        public override ICommandResult Invoke()
        {
            if (_page._lastAssistantText.Length == 0)
            {
                return _page.ShowToast("Nothing to copy yet");
            }

            _page._clipboard.SetText(_page._lastAssistantText);
            return CommandResult.KeepOpen();
        }
    }

    private sealed partial class CopyTranscriptCommand : InvokableCommand
    {
        private readonly ChatRunPage _page;

        public CopyTranscriptCommand(ChatRunPage page)
        {
            _page = page;
            Id = "little-agents.run.copy-transcript";
            Name = "Copy transcript";
        }

        public override ICommandResult Invoke()
        {
            _page._clipboard.SetText(_page._output.Body);
            return CommandResult.KeepOpen();
        }
    }

    private sealed partial class StopCommand : InvokableCommand
    {
        private readonly ChatRunPage _page;

        public StopCommand(ChatRunPage page)
        {
            _page = page;
            Id = "little-agents.run.stop";
            Name = "Stop";
        }

        public override ICommandResult Invoke()
        {
            if (_page._cts is null)
            {
                return _page.ShowToast("Nothing to stop");
            }

            _page._cts.Cancel();
            return CommandResult.KeepOpen();
        }
    }

    private sealed partial class RerunCommand : InvokableCommand
    {
        private readonly ChatRunPage _page;

        public RerunCommand(ChatRunPage page)
        {
            _page = page;
            Id = "little-agents.run.rerun";
            Name = "Re-run";
        }

        public override ICommandResult Invoke()
        {
            _page._history.Clear();
            _page._output.Body = string.Empty;
            _page._lastAssistantText = string.Empty;
            _page.StartStream(_page._initialUserMsg);
            return CommandResult.KeepOpen();
        }
    }

    private sealed partial class ReplyCommand : InvokableCommand
    {
        private readonly ChatRunPage _page;

        public ReplyCommand(ChatRunPage page)
        {
            _page = page;
            Id = "little-agents.run.reply";
            Name = "Reply";
        }

        public override ICommandResult Invoke()
        {
            _page._inputForm = new RunInputForm("Reply", replyText =>
            {
                _page._showingInput = false;
                _page._inputForm = null;
                _page.RaiseItemsChanged(0);
                _page.StartStream(replyText);
            });
            _page._showingInput = true;
            _page.RaiseItemsChanged(0);
            return CommandResult.KeepOpen();
        }
    }
}
