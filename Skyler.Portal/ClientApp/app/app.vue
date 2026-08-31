<script setup lang="ts">
import {
  ArrowUpRight,
  Bot,
  BrainCircuit,
  BriefcaseBusiness,
  CalendarDays,
  Check,
  ChevronRight,
  CircleDot,
  Clock3,
  LayoutDashboard,
  ListTree,
  Mail,
  MailClock,
  Monitor,
  Moon,
  RefreshCw,
  ShieldCheck,
  Sparkles,
  Sun,
  Users2,
  WandSparkles
} from '@lucide/vue'
import type { DashboardSummary, EvidenceAnalysis } from '~/types/dashboard'

type Period = 'day' | 'week' | 'month'
type View = 'dashboard' | 'recent' | 'approvals' | 'outlook' | 'settings'
type Theme = 'light' | 'dark' | 'system'

const periods: Array<{ value: Period; label: string }> = [
  { value: 'day', label: 'Today' },
  { value: 'week', label: '7 days' },
  { value: 'month', label: 'Month' }
]

const selectedPeriod = ref<Period>('week')
const activeView = ref<View>('dashboard')
const theme = ref<Theme>('system')
const summary = ref<DashboardSummary | null>(null)
const selectedEvidenceId = ref<string | null>(null)
const loading = ref(true)
const refreshing = ref(false)
const approvalPending = ref(false)
const errorMessage = ref<string | null>(null)

const orderedDimensions = computed(() =>
  [...(summary.value?.dimensionScores ?? [])]
    .filter((dimension) => dimension.percentage !== null)
    .sort((a, b) => (b.percentage ?? 0) - (a.percentage ?? 0))
)

const strongestDimension = computed(() => orderedDimensions.value[0] ?? null)
const selectedObservation = computed(() =>
  summary.value?.recentAnalyses.find((item) => item.evidenceId === selectedEvidenceId.value)
    ?? summary.value?.recentAnalyses[0]
    ?? null
)

const selectedDimensions = computed(() =>
  [...(selectedObservation.value?.dimensions ?? [])]
    .sort((left, right) => {
      if (left.percentage === null && right.percentage === null) return 0
      if (left.percentage === null) return 1
      if (right.percentage === null) return -1
      return right.percentage - left.percentage
    })
)

const remainingWorkPercentage = computed(() =>
  Math.max(0, Math.round((100 - (summary.value?.timeFreedPercentage ?? 0)) * 10) / 10)
)

const boundedAiPercentage = computed(() =>
  Math.min(100, Math.max(0, summary.value?.timeFreedPercentage ?? 0))
)

const pendingApprovals = computed(() =>
  (summary.value?.recentAnalyses ?? []).filter((item) =>
    item.automationOpportunity && item.estimatedTimeFreedMinutes && !item.automationApproved
  )
)

function isLeaveOrOutOfOffice(item: EvidenceAnalysis) {
  return item.isAbsent || /\b(?:annual leave|out of office)\b/i.test(item.subject)
}

const emailAnalyses = computed(() =>
  (summary.value?.recentAnalyses ?? []).filter((item) => item.kind === 'Email' && !isLeaveOrOutOfOffice(item))
)

const workToDo = computed(() =>
  emailAnalyses.value.filter((item) => item.automationOpportunity).slice(0, 6)
)

const accomplishments = computed(() =>
  (summary.value?.recentAnalyses ?? []).filter((item) => !isLeaveOrOutOfOffice(item)).slice(0, 6)
)

const meetingCount = computed(() =>
  (summary.value?.recentAnalyses ?? []).filter((item) => item.kind === 'CalendarMeeting').length
)

const emailCount = computed(() => emailAnalyses.value.length)

const userInitials = computed(() => {
  const localPart = (summary.value?.mailbox ?? 'User').split('@')[0] ?? 'User'
  const parts = localPart.split(/[._-]+/).filter(Boolean)
  if (parts.length > 1) return parts.slice(0, 2).map((part) => part[0]).join('').toUpperCase()
  return localPart.slice(0, 2).toUpperCase()
})

const viewContent: Record<View, { eyebrow: string; title: string; accent: string; description: string }> = {
  dashboard: {
    eyebrow: '',
    title: 'Your uniquely',
    accent: 'human work.',
    description: ''
  },
  recent: {
    eyebrow: '',
    title: 'All your work',
    accent: 'in one place',
    description: ''
  },
  approvals: {
    eyebrow: 'AI work approvals',
    title: 'You stay',
    accent: 'in control.',
    description: ''
  },
  outlook: {
    eyebrow: '',
    title: 'What is next,',
    accent: 'and what you accomplished.',
    description: ''
  },
  settings: {
    eyebrow: 'User controls',
    title: 'Make Skyler',
    accent: 'yours.',
    description: 'Manage your display preferences and review the Outlook account connected to this workspace.'
  }
}

const currentViewContent = computed(() => viewContent[activeView.value])

async function loadSummary(silent = false) {
  if (silent) refreshing.value = true
  else loading.value = true
  errorMessage.value = null

  try {
    if (silent) {
      await $fetch('/api/dashboard/refresh', { method: 'POST' })
    }

    const data = await $fetch<DashboardSummary>('/api/dashboard', {
      query: { period: selectedPeriod.value }
    })
    summary.value = data

    if (!selectedEvidenceId.value || !data.recentAnalyses.some((item) => item.evidenceId === selectedEvidenceId.value)) {
      selectedEvidenceId.value = data.recentAnalyses[0]?.evidenceId ?? null
    }
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'The dashboard could not be loaded.'
  } finally {
    loading.value = false
    refreshing.value = false
  }
}

async function selectPeriod(period: Period) {
  if (period === selectedPeriod.value) return
  selectedPeriod.value = period
  selectedEvidenceId.value = null
  await loadSummary()
}

async function openRecentWork(period?: Period) {
  activeView.value = 'recent'
  if (period && period !== selectedPeriod.value) await selectPeriod(period)
}

function openWork() {
  activeView.value = 'outlook'
}

async function toggleApproval(item: EvidenceAnalysis) {
  approvalPending.value = true
  errorMessage.value = null

  try {
    await $fetch(`/api/dashboard/evidence/${item.evidenceId}/automation-approval`, {
      method: 'PUT',
      body: { approved: !item.automationApproved }
    })
    // Approval only changes the local decision. Do not trigger a full Outlook sync
    // (and potentially model analysis) while the user is waiting for this button.
    await loadSummary()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'The approval could not be updated.'
  } finally {
    approvalPending.value = false
  }
}

function formatDuration(minutes: number) {
  if (minutes < 60) return `${minutes} min`
  const hours = minutes / 60
  return `${Number.isInteger(hours) ? hours : hours.toFixed(1)} hr`
}

function formatDate(value: string, includeTime = false) {
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    ...(includeTime ? { hour: 'numeric', minute: '2-digit' } : {})
  }).format(new Date(value))
}

function evidenceIcon(item: EvidenceAnalysis) {
  return item.kind === 'CalendarMeeting' ? CalendarDays : Mail
}

function applyTheme(value: Theme) {
  const resolved = value === 'system'
    ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
    : value
  document.documentElement.dataset.theme = resolved
}

function selectTheme(value: Theme) {
  theme.value = value
  localStorage.setItem('skyler-theme', value)
  applyTheme(value)
}

onMounted(() => {
  const savedTheme = localStorage.getItem('skyler-theme') as Theme | null
  if (savedTheme && ['light', 'dark', 'system'].includes(savedTheme)) theme.value = savedTheme
  applyTheme(theme.value)
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (theme.value === 'system') applyTheme('system')
  })
  loadSummary()
})
</script>

<template>
  <div class="app-shell">
    <aside class="rail">
      <nav aria-label="Primary">
        <button class="rail-button" :class="{ active: activeView === 'dashboard' }" aria-label="Dashboard" title="Dashboard" @click="activeView = 'dashboard'"><LayoutDashboard :size="19" /></button>
        <button class="rail-button" :class="{ active: activeView === 'outlook' }" aria-label="Work: next to do and accomplished" title="Work: next to do and accomplished" @click="activeView = 'outlook'"><MailClock :size="19" /></button>
        <button class="rail-button" :class="{ active: activeView === 'recent' }" aria-label="Recent work analysis" title="Recent work analysis" @click="activeView = 'recent'"><ListTree :size="19" /></button>
        <button class="rail-button" :class="{ active: activeView === 'approvals' }" aria-label="AI work pending approval" title="AI work pending approval" @click="activeView = 'approvals'">
          <Bot :size="19" />
          <span v-if="pendingApprovals.length" class="rail-badge">{{ pendingApprovals.length }}</span>
        </button>
      </nav>
      <div class="rail-footer">
        <button class="avatar" :class="{ active: activeView === 'settings' }" aria-label="User settings" title="User settings" @click="activeView = 'settings'">{{ userInitials }}</button>
      </div>
    </aside>

    <main class="dashboard">
      <header class="topbar">
        <div class="title-lockup">
          <span v-if="currentViewContent.eyebrow" class="eyebrow">{{ currentViewContent.eyebrow }}</span>
          <h1>{{ currentViewContent.title }} <em>{{ currentViewContent.accent }}</em></h1>
          <p v-if="currentViewContent.description">{{ currentViewContent.description }}</p>
        </div>

        <div v-if="activeView !== 'settings'" class="topbar-actions">
          <div class="period-switcher" role="group" aria-label="Dashboard period">
            <button
              v-for="period in periods"
              :key="period.value"
              :class="{ active: selectedPeriod === period.value }"
              @click="selectPeriod(period.value)"
            >{{ period.label }}</button>
          </div>
          <button class="icon-button" :disabled="refreshing" aria-label="Refresh dashboard" @click="loadSummary(true)">
            <RefreshCw :size="17" :class="{ spinning: refreshing }" />
          </button>
        </div>
      </header>

      <div v-if="errorMessage" class="alert" role="alert">
        <CircleDot :size="17" />
        <span>{{ errorMessage }}</span>
        <button @click="loadSummary()">Try again</button>
      </div>

      <div v-if="loading && !summary" class="loading-state" aria-live="polite">
        <span class="loading-orbit"><Sparkles :size="20" /></span>
        <strong>Reading the shape of your work…</strong>
        <small>Connecting Outlook activity with local model analysis</small>
      </div>

      <template v-else-if="summary">
        <section v-if="activeView !== 'settings'" class="status-line" aria-label="Connection status">
          <span class="live-pill"><i />{{ summary.dataMode }}</span>
          <strong class="synced-time">Synced {{ formatDate(summary.generatedAtUtc, true) }}</strong>
        </section>

        <template v-if="activeView === 'dashboard'">
        <section class="overview-grid">
          <button type="button" class="role-card card dashboard-card-link" @click="openWork">
            <div class="role-icon"><BriefcaseBusiness :size="20" /></div>
            <span class="kicker">YOUR ROLE</span>
            <h2>{{ summary.role.title ?? 'Undecided' }}</h2>
            <div class="role-meta">
              <span>Click to see upcoming and completed work <ChevronRight :size="14" /></span>
            </div>
          </button>

          <button type="button" class="signal-card card dashboard-card-link" aria-label="Open analyzed work breakdown" @click="openRecentWork()">
            <div class="card-heading">
              <div>
                <span class="kicker coral"><Sparkles :size="14" /> Strongest human signal</span>
                <h2>{{ strongestDimension?.displayName ?? 'Still observing' }}</h2>
              </div>
              <ArrowUpRight :size="18" />
            </div>
            <template v-if="strongestDimension">
              <strong class="signal-number">{{ strongestDimension.percentage }}<small>%</small></strong>
              <div class="signal-scale"><span :style="{ width: `${strongestDimension.percentage}%` }" /></div>
            </template>
            <p v-else class="empty-copy">More specific work evidence is needed before a signal can be shown.</p>
            <div class="signal-cta">
              <span>Click to see how this value was reached <ChevronRight :size="14" /></span>
            </div>
          </button>

          <button type="button" class="ai-hero card dashboard-card-link" @click="activeView = 'approvals'">
            <div class="card-heading">
              <div>
                <span class="kicker violet"><WandSparkles :size="14" /> AI contribution</span>
                <h2>Work handled for you</h2>
              </div>
              <span class="trust-chip"><ShieldCheck :size="13" /> Approved only</span>
            </div>

            <div class="ai-hero-body">
              <div class="radial-meter" :style="{ '--value': `${boundedAiPercentage * 3.6}deg` }">
                <div class="radial-core">
                  <strong>{{ summary.timeFreedPercentage }}<small>%</small></strong>
                  <span>of total volume</span>
                </div>
              </div>

              <div class="ai-volume-copy">
                <div class="big-value">{{ formatDuration(summary.timeFreedMinutes) }}</div>
                <div class="split-track" aria-hidden="true"><span :style="{ width: `${boundedAiPercentage}%` }" /></div>
                <div class="split-legend">
                  <span><i class="dot-ai" />AI {{ summary.timeFreedPercentage }}%</span>
                  <span><i class="dot-human" />Human {{ remainingWorkPercentage }}%</span>
                </div>
              </div>
            </div>

            <footer class="ai-hero-footer">
              <span><Check :size="15" />{{ summary.approvedAutomationCount }} approved</span>
              <span>{{ summary.pendingAutomationApprovalCount }} awaiting your review <ChevronRight :size="14" /></span>
            </footer>
          </button>
        </section>

        <section class="metrics-row" aria-label="Work overview">
          <article>
            <span class="metric-icon mint"><BrainCircuit :size="18" /></span>
            <div><strong>{{ summary.decidedObservationCount }}</strong><span>Decided observations</span></div>
          </article>
          <article>
            <span class="metric-icon blue"><Clock3 :size="18" /></span>
            <div><strong>{{ summary.automationOpportunityCount }}</strong><span>Automation opportunities</span></div>
          </article>
        </section>

        <section class="content-grid">
          <article class="dimensions-card card">
            <div class="section-heading">
              <div><span class="kicker">Human advantage</span><h2>Signal profile</h2></div>
            </div>
            <div class="dimension-list">
              <div v-for="(dimension, index) in orderedDimensions" :key="dimension.dimension" class="dimension-row">
                <span class="dimension-rank">0{{ index + 1 }}</span>
                <div class="dimension-copy">
                  <div><strong>{{ dimension.displayName }}</strong><span>{{ dimension.percentage }}%</span></div>
                  <div class="dimension-track"><span :style="{ width: `${dimension.percentage}%` }" /></div>
                </div>
              </div>
              <p v-if="orderedDimensions.length === 0" class="empty-copy">No dimension scores are available yet.</p>
            </div>
          </article>

          <article class="ai-log-card card">
            <div class="section-heading">
              <div><span class="kicker violet">AI work log</span><h2>AI APPROVALS</h2></div>
              <span class="count-badge">{{ summary.aiWorkItems.length }}</span>
            </div>
            <div v-if="summary.aiWorkItems.length" class="ai-log-list">
              <div v-for="item in summary.aiWorkItems" :key="item.evidenceId" class="ai-log-row">
                <span class="spark-box"><Sparkles :size="16" /></span>
                <div><strong>{{ item.workDescription }}</strong><span>{{ item.subject }} · {{ formatDate(item.approvedAtUtc) }}</span></div>
                <b>{{ formatDuration(item.minutes) }}</b>
              </div>
            </div>
            <div v-else class="empty-ai-log">
              <span class="empty-spark"><Sparkles :size="22" /></span>
              <strong>No AI work approved yet</strong>
              <p>Review an opportunity below to begin tracking AI contribution.</p>
            </div>
          </article>
        </section>
        </template>

        <section v-else-if="activeView === 'recent'" class="evidence-section view-section">
          <div class="section-heading evidence-title">
            <div><h2>Work analysis breakdown</h2></div>
            <span class="section-caption">{{ summary.recentAnalyses.length }} analyzed work items</span>
          </div>

          <div class="view-metrics" aria-label="Recent work totals">
            <article><span class="metric-icon blue"><Mail :size="18" /></span><div><strong>{{ emailCount }}</strong><span>Sent emails</span></div></article>
            <article><span class="metric-icon mint"><CalendarDays :size="18" /></span><div><strong>{{ meetingCount }}</strong><span>Meetings</span></div></article>
            <article><span class="metric-icon peach"><Users2 :size="18" /></span><div><strong>{{ formatDuration(summary.mentorshipMinutes) }}</strong><span>Mentorship</span></div></article>
          </div>

          <div class="evidence-layout">
            <div class="evidence-list card">
              <button
                v-for="item in summary.recentAnalyses"
                :key="item.evidenceId"
                class="evidence-row"
                :class="{ selected: selectedObservation?.evidenceId === item.evidenceId }"
                @click="selectedEvidenceId = item.evidenceId"
              >
                <span class="evidence-icon"><component :is="evidenceIcon(item)" :size="16" /></span>
                <span class="evidence-copy"><strong>{{ item.subject }}</strong></span>
                <span class="evidence-date">{{ formatDate(item.occurredAtUtc) }}</span>
              </button>
              <p v-if="summary.recentAnalyses.length === 0" class="empty-copy padded">No Outlook evidence was found for this period.</p>
            </div>

            <aside v-if="selectedObservation" class="detail-card card">
              <div class="detail-topline">
                <span v-if="selectedObservation.kind === 'CalendarMeeting'" class="source-chip"><CalendarDays :size="13" />Meeting</span>
                <span class="detail-date">{{ formatDate(selectedObservation.occurredAtUtc, true) }}</span>
              </div>
              <h3>{{ selectedObservation.subject }}</h3>

              <section class="conclusions-block" aria-labelledby="conclusions-title">
                <div class="conclusions-heading">
                  <div><span>Dashboard conclusions</span><strong id="conclusions-title">Why this work shaped the scores</strong></div>
                  <span>{{ selectedDimensions.length }}</span>
                </div>
                <div v-if="selectedDimensions.length" class="conclusion-list">
                  <article v-for="dimension in selectedDimensions" :key="dimension.dimension" class="conclusion-row">
                    <div class="conclusion-topline">
                      <strong>{{ dimension.displayName }}</strong>
                      <span>{{ dimension.percentage === null ? 'Not scored' : `${dimension.percentage}%` }}</span>
                    </div>
                    <div class="conclusion-track" aria-hidden="true"><span :style="{ width: `${dimension.percentage ?? 0}%` }" /></div>
                    <p>{{ dimension.rationale }}</p>
                  </article>
                </div>
                <p v-else class="empty-copy">No dashboard conclusions were reached for this work item.</p>
              </section>

              <div class="automation-block">
                <div class="automation-title">
                  <span class="spark-box"><WandSparkles :size="16" /></span>
                  <div>
                    <small>Suggested AI Opportunity</small>
                    <strong>{{ selectedObservation.automationOpportunity ?? 'No AI opportunity identified' }}</strong>
                  </div>
                </div>
                <div v-if="selectedObservation.automationOpportunity" class="approval-row">
                  <span>Estimated {{ formatDuration(selectedObservation.estimatedTimeFreedMinutes ?? 0) }}</span>
                  <button
                    class="approve-button"
                    :class="{ approved: selectedObservation.automationApproved }"
                    :disabled="approvalPending"
                    @click="toggleApproval(selectedObservation)"
                  >
                    <Check v-if="selectedObservation.automationApproved" :size="14" />
                    {{ selectedObservation.automationApproved ? 'Approved' : 'Approve AI work' }}
                  </button>
                </div>
              </div>
            </aside>
          </div>
        </section>

        <section v-else-if="activeView === 'approvals'" class="view-section approvals-view">
          <div class="section-heading evidence-title">
            <div><span class="kicker violet">Review queue</span><h2>Pending AI work</h2></div>
            <span class="count-badge">{{ pendingApprovals.length }}</span>
          </div>

          <div v-if="pendingApprovals.length" class="approval-list">
            <article v-for="item in pendingApprovals" :key="item.evidenceId" class="approval-card card">
              <span class="spark-box"><WandSparkles :size="17" /></span>
              <div class="approval-copy">
                <div class="approval-context"><span>{{ item.kind === 'CalendarMeeting' ? 'Meeting' : 'Email' }}</span><span>{{ formatDate(item.occurredAtUtc) }}</span></div>
                <h3>{{ item.automationOpportunity }}</h3>
                <p>{{ item.subject }}</p>
                <small>{{ item.summary }}</small>
              </div>
              <div class="approval-action">
                <strong>{{ formatDuration(item.estimatedTimeFreedMinutes ?? 0) }}</strong>
                <span>estimated saving</span>
                <button class="approve-button" :disabled="approvalPending" @click="toggleApproval(item)"><Check :size="14" />Approve</button>
              </div>
            </article>
          </div>
          <div v-else class="empty-view card">
            <span class="empty-spark"><ShieldCheck :size="23" /></span>
            <strong>You are all caught up</strong>
            <p>No AI work is waiting for your approval in this period.</p>
          </div>

          <div v-if="summary.aiWorkItems.length" class="approved-history card">
            <div class="section-heading"><div><span class="kicker">Approved history</span><h2>Recently approved</h2></div><span class="section-caption">{{ summary.aiWorkItems.length }} items</span></div>
            <div class="ai-log-list">
              <div v-for="item in summary.aiWorkItems" :key="item.evidenceId" class="ai-log-row">
                <span class="spark-box"><Check :size="16" /></span>
                <div><strong>{{ item.workDescription }}</strong><span>{{ item.subject }} · {{ formatDate(item.approvedAtUtc) }}</span></div>
                <b>{{ formatDuration(item.minutes) }}</b>
              </div>
            </div>
          </div>
        </section>

        <section v-else-if="activeView === 'outlook'" class="view-section outlook-view">
          <div class="summary-columns">
            <article class="summary-panel card">
              <div class="section-heading">
                <div><span class="kicker coral"><MailClock :size="14" /> Coming up</span><h2>Work to do</h2></div>
                <span class="count-badge">{{ workToDo.length }}</span>
              </div>
              <div v-if="workToDo.length" class="summary-list">
                <article v-for="item in workToDo" :key="item.evidenceId">
                  <span class="summary-icon work-to-do"><MailClock :size="16" /></span>
                  <div><strong>{{ item.automationOpportunity }}</strong><span>{{ item.subject }} · {{ formatDate(item.occurredAtUtc) }}</span></div>
                </article>
              </div>
              <p v-else class="empty-copy">No work to do could be inferred from mail in this period.</p>
            </article>

            <article class="summary-panel card">
              <div class="section-heading">
                <div><span class="kicker violet"><Check :size="14" /> Completed</span><h2>What you accomplished</h2></div>
                <span class="count-badge">{{ accomplishments.length }}</span>
              </div>
              <p class="panel-intro">A concise record of the work reflected in your analyzed Outlook activity.</p>
              <div v-if="accomplishments.length" class="summary-list">
                <article v-for="item in accomplishments" :key="item.evidenceId">
                  <span class="summary-icon complete"><Check :size="15" /></span>
                  <div><strong>{{ item.summary }}</strong><span>{{ item.subject }} · {{ formatDate(item.occurredAtUtc) }}</span></div>
                </article>
              </div>
              <p v-else class="empty-copy">No completed work was found in this period.</p>
            </article>
          </div>
        </section>

        <section v-else class="view-section settings-view">
          <article class="settings-card card">
            <div class="settings-heading">
              <span class="settings-avatar">{{ userInitials }}</span>
              <div><span class="kicker">Connected user</span><h2>{{ summary.mailbox }}</h2><p>This is the Outlook mailbox Skyler uses for your work intelligence.</p></div>
            </div>

            <div class="setting-row">
              <div><strong>Appearance</strong><span>Choose how Skyler looks on this device.</span></div>
              <div class="theme-switcher" role="group" aria-label="Appearance">
                <button :class="{ active: theme === 'light' }" @click="selectTheme('light')"><Sun :size="16" />Light</button>
                <button :class="{ active: theme === 'dark' }" @click="selectTheme('dark')"><Moon :size="16" />Dark</button>
                <button :class="{ active: theme === 'system' }" @click="selectTheme('system')"><Monitor :size="16" />System</button>
              </div>
            </div>

            <div class="setting-row account-row">
              <div><strong>Associated email</strong><span>The account currently authorized for Outlook analysis.</span></div>
              <span class="email-chip"><Mail :size="16" />{{ summary.mailbox }}</span>
            </div>

          </article>
        </section>
      </template>
    </main>
  </div>
</template>
