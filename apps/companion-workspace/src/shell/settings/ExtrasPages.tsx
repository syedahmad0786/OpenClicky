import type { SettingsAction } from "../contract";
import { Group, PillButton, Row } from "../ui";
import { act, Page } from "./common";

/** One group, one description row, one pill that opens (or exports) the native thing. */
function Simple({ title, subtitle, row, description, button, action }: {
  title: string; subtitle: string; row: string; description: string; button: string; action: SettingsAction;
}) {
  return (
    <Page title={title} subtitle={subtitle}>
      <Group>
        <Row title={row} description={description}>
          <PillButton onClick={() => act(action)}>{button}</PillButton>
        </Row>
      </Group>
    </Page>
  );
}

export const MemoryPage = () => (
  <Simple title="Memory" subtitle="What OpenClicky has learned about you." row="Memories"
    description="Short notes about your preferences and projects, kept on this PC. Export them to read or back them up."
    button="Export memories" action="exportMemory" />
);

export const NotesPage = () => (
  <Simple title="Notes" subtitle="Your local notes." row="Notes" description="Open your notes in their own window." button="Open" action="openNotes" />
);

export const ClipboardPage = () => (
  <Simple title="Clipboard shelf" subtitle="Text you've collected to paste later." row="Clipboard shelf"
    description="Open the shelf to copy back anything you've collected." button="Open" action="openClipboard" />
);

export const TasksPage = () => (
  <Simple title="Tasks" subtitle="History of desktop tasks and their steps." row="Task history"
    description="Open the timeline of what OpenClicky has done on this PC." button="Open" action="openTasks" />
);
