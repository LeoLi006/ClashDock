using ClashDock;
using System.Reflection;
internal static class SubscriptionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var method=typeof(RoutingService).GetMethod("SubscriptionMatches",BindingFlags.NonPublic|BindingFlags.Static)!;
        bool Match(string yaml,params string[] rules)=>(bool)method.Invoke(null,[yaml,rules])!;
        const string source="rules:\n- IP-CIDR6,2001:db8::/32,DIRECT,no-resolve\n- DOMAIN,example.com,proxy\n- MATCH,DIRECT\n";
        string[] live=["IPCIDR|2001:db8::/32|DIRECT","Domain|example.com|proxy","Match||DIRECT"];
        check(Match(source,live),"Mihomo IPv6 IPCIDR alias preserves equivalent subscription");
        check(!Match(source,live.Select(x=>x.Replace("2001:db8::/32","2001:db9::/32")).ToArray()),"Changed IPv6 payload remains rejected");
        check(!Match(source,live.Select(x=>x.Replace("/32|DIRECT","/32|proxy")).ToArray()),"Changed IPv6 target remains rejected");
        check(!Match(source,live.Reverse().ToArray()),"Changed rule order remains rejected");
        check(!Match(source,live.Take(2).ToArray()),"Missing subscription rule remains rejected");
        check(!Match("rules:\n- IP-CIDR6,192.0.2.0/24,DIRECT\n","IPCIDR|192.0.2.0/24|DIRECT"),"IPv4 cannot masquerade as IPv6 alias");
        check(!Match("rules:\n- IP-CIDR6,2001:db8::/129,DIRECT\n","IPCIDR|2001:db8::/129|DIRECT"),"Invalid IPv6 prefix remains rejected");
        check(Match("rules:\n- GEOIP,CN,DIRECT\n","GeoIP|cn|DIRECT"),"Country code API casing is equivalent");
        check(!Match("rules:\n- GEOIP,CN,DIRECT\n","GeoIP|us|DIRECT"),"Changed country remains rejected");
        check(!Match("rules:\n- GEOIP,CN,Proxy\n","GeoIP|cn|proxy"),"Proxy target identity remains case sensitive");
        Console.WriteLine("10 subscription boundary checks passed.");
    }
}
