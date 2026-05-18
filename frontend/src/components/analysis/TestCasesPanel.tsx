import { useState } from "react";
import { Copy, ChevronDown, ChevronRight } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";

interface SecurityTestScenario {
  scenarioTitle: string;
  given: string;
  when: string;
  then: string;
  and?: string | null;
}

interface SecurityTestCase {
  threatIdentifier: string;
  threatTitle: string;
  scenarios: SecurityTestScenario[];
}

interface TestCasesPanelProps {
  testCases: SecurityTestCase[];
  onThreatClick?: (identifier: string) => void;
}

function toGherkin(tc: SecurityTestCase): string {
  return tc.scenarios
    .map((s) => {
      const lines = [
        `Feature: ${tc.threatTitle}`,
        ``,
        `  Scenario: ${s.scenarioTitle}`,
        `    Given ${s.given}`,
        `    When ${s.when}`,
        `    Then ${s.then}`,
        ...(s.and ? [`    And ${s.and}`] : []),
      ];
      return lines.join("\n");
    })
    .join("\n\n");
}

function ScenarioBlock({ scenario }: { scenario: SecurityTestScenario }) {
  return (
    <div className="rounded-md bg-muted/40 px-4 py-3 font-mono text-xs space-y-0.5">
      <p className="font-semibold text-muted-foreground not-italic"># {scenario.scenarioTitle}</p>
      <p><span className="text-purple-600 dark:text-purple-400 font-semibold">Given</span> {scenario.given}</p>
      <p><span className="text-blue-600 dark:text-blue-400 font-semibold">When</span> {scenario.when}</p>
      <p><span className="text-green-600 dark:text-green-400 font-semibold">Then</span> {scenario.then}</p>
      {scenario.and && (
        <p><span className="text-green-600 dark:text-green-400 font-semibold">And</span> {scenario.and}</p>
      )}
    </div>
  );
}

function TestCaseRow({
  tc,
  onThreatClick,
}: {
  tc: SecurityTestCase;
  onThreatClick?: ((id: string) => void) | undefined;
}) {
  const [open, setOpen] = useState(true);

  return (
    <div className="rounded-lg border">
      <button
        className="flex w-full items-center gap-2 px-4 py-3 text-left hover:bg-muted/30 transition-colors"
        onClick={() => setOpen((v) => !v)}
      >
        {open ? (
          <ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground" />
        ) : (
          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
        )}
        <button
          onClick={(e) => { e.stopPropagation(); onThreatClick?.(tc.threatIdentifier); }}
          className="rounded bg-muted px-1.5 py-0.5 text-xs font-mono font-bold hover:bg-primary/10 hover:text-primary transition-colors shrink-0"
        >
          {tc.threatIdentifier}
        </button>
        <span className="flex-1 truncate text-sm font-medium">{tc.threatTitle}</span>
        <span className="shrink-0 text-xs text-muted-foreground">{tc.scenarios.length} scenario{tc.scenarios.length !== 1 ? "s" : ""}</span>
        <button
          title="Copy Gherkin"
          onClick={(e) => { e.stopPropagation(); void navigator.clipboard.writeText(toGherkin(tc)); toast.success("Copied"); }}
          className="shrink-0 rounded p-1 text-muted-foreground hover:bg-muted hover:text-foreground transition-colors"
        >
          <Copy className="h-3.5 w-3.5" />
        </button>
      </button>
      {open && (
        <div className="border-t px-4 pb-4 pt-3 space-y-2">
          {tc.scenarios.map((s, i) => (
            <ScenarioBlock key={i} scenario={s} />
          ))}
        </div>
      )}
    </div>
  );
}

export function TestCasesPanel({ testCases, onThreatClick }: TestCasesPanelProps) {
  if (!testCases.length) {
    return (
      <div className="flex items-center justify-center p-12 text-center text-muted-foreground text-sm">
        No security test cases generated for this analysis.
      </div>
    );
  }

  function copyAll() {
    const text = testCases.map(toGherkin).join("\n\n---\n\n");
    void navigator.clipboard.writeText(text);
    toast.success(`${testCases.length} test case${testCases.length !== 1 ? "s" : ""} copied`);
  }

  return (
    <div className="p-4 space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          {testCases.length} threat{testCases.length !== 1 ? "s" : ""} with security test scenarios
        </p>
        <button
          onClick={copyAll}
          className={cn(
            "flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs font-medium transition-colors",
            "text-muted-foreground hover:bg-muted hover:text-foreground",
          )}
        >
          <Copy className="h-3.5 w-3.5" />
          Copy all Gherkin
        </button>
      </div>
      {testCases.map((tc) => (
        <TestCaseRow key={tc.threatIdentifier} tc={tc} onThreatClick={onThreatClick} />
      ))}
    </div>
  );
}
