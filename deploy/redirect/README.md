# Temporary Hobthrush domain redirect

The rule payload in rule.json sends requests for hobthrush.com and www.hobthrush.com to the GitHub repository with a 302; query strings are not preserved. The DNS payload in dns.json contains proxied placeholder A records so Cloudflare can apply the redirect without an origin server. Replace the redirect when the website is ready to use these domains.

Apply the DNS records and add the rule in the hobthrush.com zone using a zone-scoped API token with DNS: Edit and Single Redirect: Edit permissions.
