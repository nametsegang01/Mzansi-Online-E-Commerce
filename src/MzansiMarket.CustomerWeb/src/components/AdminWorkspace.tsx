import { useCallback, useEffect, useMemo, useState } from 'react'
import { BadgeCheck, Ban, Building2, Clock3, LoaderCircle, LogOut, RefreshCw, ShieldCheck, UserCheck, UserX } from 'lucide-react'
import { api, ApiError } from '../api/client'
import type { CurrentUser, SellerApplication } from '../api/types'

type Filter = 'All' | 'Pending' | 'Approved' | 'Rejected' | 'Suspended'
type Decision = 'Approve' | 'Reject' | 'Suspend'
const filters: Filter[] = ['All', 'Pending', 'Approved', 'Rejected', 'Suspended']
const date = new Intl.DateTimeFormat('en-ZA', { dateStyle: 'medium', timeStyle: 'short' })

export function AdminWorkspace({ user, announce, onSignOut }: { user: CurrentUser; announce: (text: string) => void; onSignOut: () => Promise<void> }) {
  const [applications, setApplications] = useState<SellerApplication[]>([])
  const [filter, setFilter] = useState<Filter>('Pending')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true); setError('')
    try { setApplications(await api.adminSellerApplications()) }
    catch (value) { setError(value instanceof ApiError ? value.message : 'The reseller applications could not be loaded.') }
    finally { setLoading(false) }
  }, [])
  useEffect(() => { void load() }, [load])

  const visible = useMemo(() => filter === 'All' ? applications : applications.filter(item => item.sellerStatus === filter), [applications, filter])
  const count = (status: Filter) => status === 'All' ? applications.length : applications.filter(item => item.sellerStatus === status).length

  async function decide(application: SellerApplication, action: Decision) {
    setBusy(`${application.sellerId}-${action}`); setError('')
    try {
      const updated = await api.decideSellerApplication(application.sellerId, action)
      setApplications(current => current.map(item => item.sellerId === updated.sellerId ? updated : item))
      announce(`${updated.tradingName} is now ${updated.sellerStatus.toLowerCase()}.`)
    } catch (value) { setError(value instanceof ApiError ? value.message : 'The reseller status could not be changed.') }
    finally { setBusy('') }
  }

  return <section className="workspace admin-workspace section-wrap">
    <div className="admin-heading"><div><p className="eyebrow"><ShieldCheck/> Administration</p><h1>Reseller approvals</h1><p>Review applications and control which businesses can publish products to Mzansi Market.</p></div><div className="admin-session"><span className="avatar">{user.displayName[0]}</span><p><strong>{user.displayName}</strong><small>System administrator</small></p><button className="secondary-button compact" onClick={onSignOut}><LogOut/> Sign out</button></div></div>
    <div className="admin-summary" aria-label="Reseller application summary"><div><Clock3/><span><strong>{count('Pending')}</strong>Awaiting review</span></div><div><BadgeCheck/><span><strong>{count('Approved')}</strong>Approved</span></div><div><Ban/><span><strong>{count('Suspended')}</strong>Suspended</span></div></div>
    <div className="admin-toolbar"><nav className="workspace-tabs" aria-label="Filter reseller applications">{filters.map(status => <button key={status} aria-current={filter === status ? 'page' : undefined} onClick={() => setFilter(status)}>{status}<span>{count(status)}</span></button>)}</nav><button className="secondary-button compact" disabled={loading} onClick={() => void load()}><RefreshCw className={loading ? 'spin' : ''}/> Refresh</button></div>
    {error && <div className="notice notice--error" role="alert">{error}</div>}
    {loading ? <div className="loading-state" role="status"><LoaderCircle className="spin"/> Loading reseller applications…</div> : visible.length === 0 ? <div className="empty-state admin-empty"><UserCheck/><h2>No {filter.toLowerCase()} applications</h2><p>Applications will appear here as reseller statuses change.</p></div> : <div className="application-grid">{visible.map(application => <article className="application-card" key={application.sellerId}>
      <header><span className="business-mark"><Building2/></span><div><p className="eyebrow">{application.storeName}</p><h2>{application.tradingName}</h2></div><span className={`admin-status admin-status--${application.sellerStatus.toLowerCase()}`}>{application.sellerStatus}</span></header>
      <dl><div><dt>Applicant</dt><dd>{application.displayName}</dd></div><div><dt>Email</dt><dd>{application.email}</dd></div><div><dt>Registration</dt><dd>{application.registrationNumber || 'Not provided'}</dd></div><div><dt>Applied</dt><dd>{date.format(new Date(application.createdAt))}</dd></div><div><dt>Store status</dt><dd>{application.storeStatus}</dd></div></dl>
      <footer>{application.sellerStatus !== 'Approved' && <button className="primary-button compact" disabled={Boolean(busy)} onClick={() => void decide(application, 'Approve')}><UserCheck/> {busy === `${application.sellerId}-Approve` ? 'Approving…' : 'Approve'}</button>}{application.sellerStatus === 'Pending' && <button className="secondary-button compact danger" disabled={Boolean(busy)} onClick={() => void decide(application, 'Reject')}><UserX/> Reject</button>}{application.sellerStatus === 'Approved' && <button className="secondary-button compact danger" disabled={Boolean(busy)} onClick={() => void decide(application, 'Suspend')}><Ban/> Suspend</button>}</footer>
    </article>)}</div>}
  </section>
}
