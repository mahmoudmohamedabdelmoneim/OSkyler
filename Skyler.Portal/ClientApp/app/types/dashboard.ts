export interface DashboardSummary {
  generatedAtUtc: string
  dataMode: string
  mailbox: string
  period: string
  evidenceCount: number
  decidedObservationCount: number
  timeFreedMinutes: number
  workdayBaselineMinutes: number
  periodBaselineMinutes: number
  timeFreedPercentage: number
  automationOpportunityCount: number
  approvedAutomationCount: number
  pendingAutomationApprovalCount: number
  absenceObservationCount: number
  mentorshipMinutes: number
  aiWorkItems: AiWorkItem[]
  role: RoleSummary
  dimensionScores: DimensionSummary[]
  recentAnalyses: EvidenceAnalysis[]
}

export interface AiWorkItem {
  evidenceId: string
  subject: string
  workDescription: string
  minutes: number
  approvedAtUtc: string
}

export interface RoleSummary {
  decision: string
  title: string | null
  confidence: number
  evidenceCount: number
  rationale: string
}

export interface DimensionSummary {
  dimension: string
  displayName: string
  percentage: number | null
  confidence: number | null
  evidenceCount: number
}

export interface EvidenceAnalysis {
  evidenceId: string
  source: string
  kind: string
  subject: string
  occurredAtUtc: string
  durationMinutes: number | null
  isMentorship: boolean
  isSynthetic: boolean
  isAbsent: boolean
  summary: string
  automationOpportunity: string | null
  analyzer: string
  usedLocalModel: boolean
  estimatedTimeFreedMinutes: number | null
  automationApproved: boolean
  automationApprovedAtUtc: string | null
  timeFreedMinutes: number
  warning: string | null
  dimensions: DimensionScore[]
}

export interface DimensionScore {
  dimension: string
  displayName: string
  percentage: number | null
  confidence: number
  rationale: string
}
