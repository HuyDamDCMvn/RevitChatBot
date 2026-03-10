import { SkillInfo } from '../types/messages';

interface Props {
  skill: SkillInfo;
}

export function SkillPanel({ skill }: Props) {
  const isExecuting = skill.status === 'executing';

  return (
    <div className="mx-3 mb-2 rounded-lg border border-revit-200 bg-revit-50 px-3 py-2 text-xs">
      <div className="flex items-center gap-2">
        {isExecuting ? (
          <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-revit-400 border-t-transparent" />
        ) : skill.result?.success ? (
          <span className="text-green-500">&#10003;</span>
        ) : (
          <span className="text-red-500">&#10007;</span>
        )}
        <span className="font-medium text-revit-800">
          {formatSkillName(skill.name)}
        </span>
        {skill.result && (
          <span className="text-gray-500">- {skill.result.message}</span>
        )}
      </div>
    </div>
  );
}

function formatSkillName(name: string): string {
  return name
    .replace(/_/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase());
}
