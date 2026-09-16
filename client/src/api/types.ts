export type Difficulty = 'Easy' | 'Medium' | 'Hard'
export type QuestionType = 'SingleChoice' | 'MultipleChoice'
export type QuestionSource = 'Ai' | 'Seed' | 'Manual' | 'Reference'
/** Review serves only the questions whose most recent answer was wrong. */
export type ExamMode = 'Practice' | 'Exam' | 'Review'

export interface ReviewCount {
  certificationCode: string
  count: number
}

export interface Domain {
  id: number
  name: string
  weightPercent: number
  questionCount: number
}

export interface Certification {
  id: number
  code: string
  name: string
  description: string
  passingScore: number
  examQuestionCount: number
  durationMinutes: number
  questionCount: number
  domains: Domain[]
}

export interface Option {
  label: string
  text: string
  isCorrect: boolean | null
}

export interface Question {
  id: string
  stem: string
  type: QuestionType
  difficulty: Difficulty
  source: QuestionSource
  domainName: string | null
  serviceTags: string | null
  explanation: string | null
  options: Option[]
}

export interface GenerateRequest {
  certificationCode: string
  domainId?: number | null
  difficulty: Difficulty
  count: number
  topicHint?: string | null
}

export interface GenerateResponse {
  provider: string
  model: string
  requested: number
  created: number
  duplicates: number
  rejected: number
  questions: Question[]
  warning: string | null
}

export interface ExamItem {
  order: number
  questionId: string
  question: Question
  selectedLabels: string | null
  isCorrect: boolean | null
}

export interface ExamSession {
  id: string
  certificationCode: string
  certificationName: string
  mode: ExamMode
  durationMinutes: number
  passingScore: number
  startedAt: string
  completedAt: string | null
  scorePercent: number | null
  passed: boolean | null
  items: ExamItem[]
}

export interface AnswerResponse {
  isCorrect: boolean
  correctLabels: string[]
  explanation: string
  revealAnswer: boolean
}

export interface DomainScore {
  domain: string
  correct: number
  total: number
  accuracyPercent: number
}

export interface ExamResult {
  sessionId: string
  certificationCode: string
  scorePercent: number
  passed: boolean
  passingScore: number
  correct: number
  total: number
  unanswered: number
  secondsSpent: number
  domainBreakdown: DomainScore[]
}

export interface DomainReadiness {
  domain: string
  weightPercent: number
  answered: number
  observedAccuracy: number
  predictedAccuracy: number
  status: 'strong' | 'on-track' | 'needs-work' | 'weak' | 'no-data'
}

export interface Readiness {
  certificationCode: string
  method: string
  predictedScorePercent: number
  passingScore: number
  likelyToPass: boolean
  confidence: number
  answersAnalyzed: number
  domains: DomainReadiness[]
  recommendations: string[]
}

// ---------- lessons ----------

export type MasteryStatus = 'Untested' | 'Weak' | 'Learning' | 'Strong'

/** Whether the accuracy figure was measured over this topic's own service or its whole domain. */
export type MasteryBasis = 'Topic' | 'Domain'

export interface LessonMastery {
  answered: number
  correct: number
  /** Null until enough questions on this topic have been answered for the figure to mean anything. */
  accuracyPercent: number | null
  status: MasteryStatus
  basis: MasteryBasis
}

/** What a lesson teaches: something you deploy, an idea you apply, or something you buy. */
export type LessonKind = 'Service' | 'Concept' | 'Commercial'

export interface LessonSummary {
  slug: string
  title: string
  category: string
  kind: LessonKind
  domainName: string | null
  purpose: string
  isCore: boolean
  /** True once the detailed notes have been generated and cached for this topic. */
  hasNotes: boolean
  completed: boolean
  mastery: LessonMastery
}

export interface LessonsResponse {
  certificationCode: string
  certificationName: string
  totalTopics: number
  completedTopics: number
  /** False when no provider is set up, so the UI can explain why notes are missing. */
  aiConfigured: boolean
  /** Already ordered by what to study next — the server does the ranking. */
  topics: LessonSummary[]
}

export interface LessonBody {
  overview: string
  useCases: string[]
  costNotes: string
  integrations: string[]
  realWorldExample: string
  examTraps: string[]
  provider: string
  model: string
  generatedAt: string
}

export interface LessonLink {
  slug: string
  title: string
}

export interface LessonDetail {
  slug: string
  title: string
  category: string
  domainName: string | null
  domainWeightPercent: number | null
  purpose: string
  pricingModel: string
  docsUrl: string
  pricingUrl: string | null
  mastery: LessonMastery
  completed: boolean
  /**
   * Null until the notes have been generated for this topic. Reading a lesson never generates
   * them, so the page renders the verified facts first and asks for the notes separately.
   */
  body: LessonBody | null
  aiConfigured: boolean
  warning: string | null
  practiceQuestions: Question[]
  related: LessonLink[]
  /** Curriculum order, so "next" means the same thing on every visit. Null at the ends. */
  previous: LessonLink | null
  next: LessonLink | null
}

export interface TutorTurn {
  question: string
  answer: string
}

export interface TutorAnswer {
  slug: string
  title: string
  answer: string
  /** Who answered — shown so an answer is never mistaken for verified fact. */
  provider: string
  model: string
}

export interface LessonProgress {
  slug: string
  completed: boolean
  completedAt: string | null
}
