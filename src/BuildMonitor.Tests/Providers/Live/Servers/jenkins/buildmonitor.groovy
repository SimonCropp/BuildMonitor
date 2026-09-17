// Runs at every start. It switches security on, with one local user and the API token that
// provision.sh generated. Anonymous users may read nothing, as on a private server. A request
// that loses its credential on a redirect then fails here as it would for a user.
import hudson.security.FullControlOnceLoggedInAuthorizationStrategy
import hudson.security.HudsonPrivateSecurityRealm
import hudson.model.User
import jenkins.model.Jenkins
import jenkins.model.JenkinsLocationConfiguration
import jenkins.security.ApiTokenProperty

def secret = new Properties()
new File('/run/secrets/buildmonitor_jenkins').withReader { secret.load(it) }
def name = secret.getProperty('user')

def jenkins = Jenkins.get()
def realm = jenkins.securityRealm instanceof HudsonPrivateSecurityRealm
    ? jenkins.securityRealm
    : new HudsonPrivateSecurityRealm(false)
realm.createAccount(name, secret.getProperty('password'))
jenkins.securityRealm = realm
def strategy = new FullControlOnceLoggedInAuthorizationStrategy()
strategy.allowAnonymousRead = false
jenkins.authorizationStrategy = strategy
jenkins.numExecutors = 2
jenkins.save()

def url = System.getenv('BUILDMONITOR_ROOT_URL')
if (url) {
    def location = JenkinsLocationConfiguration.get()
    location.url = url
    location.save()
}

// The token's value is fixed by provision.sh, so a workflow masks it before Jenkins has started.
def user = User.getById(name, true)
def tokens = user.getProperty(ApiTokenProperty)
if (tokens == null) {
    tokens = new ApiTokenProperty()
    user.addProperty(tokens)
}
tokens.tokenList.findAll { it.name == 'buildmonitor-live' }.each { tokens.revokeToken(it.uuid) }
tokens.addFixedNewToken('buildmonitor-live', secret.getProperty('token'))
user.save()
