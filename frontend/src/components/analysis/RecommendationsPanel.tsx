import { Badge } from "@/components/ui/badge";

interface Recommendation {
  title: string;
  description: string;
  principles?: string[];
  affectedElements?: string[];
}

interface RecommendationsPanelProps {
  recommendations: Recommendation[];
}

export function RecommendationsPanel({ recommendations }: RecommendationsPanelProps) {
  if (!recommendations.length) {
    return (
      <div className="flex items-center justify-center p-12 text-center text-muted-foreground text-sm">
        No secure design recommendations in this analysis.
      </div>
    );
  }

  return (
    <div className="space-y-3 p-4">
      {recommendations.map((rec, idx) => (
        <div key={idx} className="rounded-lg border p-4 space-y-2">
          <h4 className="font-medium">{rec.title}</h4>
          <p className="text-sm text-muted-foreground">{rec.description}</p>
          {rec.principles && rec.principles.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {rec.principles.map((p) => (
                <Badge key={p} variant="outline" className="text-xs">{p}</Badge>
              ))}
            </div>
          )}
          {rec.affectedElements && rec.affectedElements.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {rec.affectedElements.map((e) => (
                <Badge key={e} variant="secondary" className="text-xs">{e}</Badge>
              ))}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
