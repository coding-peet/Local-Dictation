package local.palanote.phone;

import android.content.*;
import android.app.job.*;
import org.json.*;
import java.io.*;
import java.net.URL;
import java.security.*;
import java.security.cert.CertificateFactory;
import java.util.*;
import javax.net.ssl.*;

final class Store {
    static android.content.SharedPreferences prefs(Context c) {
        return c.getSharedPreferences("pala", Context.MODE_PRIVATE);
    }

    static byte[] bytes(InputStream in) throws IOException {
        try (InputStream source = in; ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            byte[] chunk = new byte[8192];
            for (int n; (n = source.read(chunk)) != -1;) out.write(chunk, 0, n);
            return out.toByteArray();
        }
    }

    static JSONObject config(Context c) throws JSONException {
        return new JSONObject(prefs(c).getString("pair", "{}"));
    }

    static synchronized void sync(Context c) throws Exception {
        JSONObject config = config(c);
        KeyStore keys = KeyStore.getInstance(KeyStore.getDefaultType());
        keys.load(null);
        keys.setCertificateEntry("pala", CertificateFactory.getInstance("X.509").generateCertificate(
            new ByteArrayInputStream(config.getString("cert").getBytes("UTF-8"))));
        TrustManagerFactory managers = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm());
        managers.init(keys);
        SSLContext tls = SSLContext.getInstance("TLS");
        tls.init(null, managers.getTrustManagers(), null);
        fetch(c, config, tls, "/pala/shopping", "items");
        fetch(c, config, tls, "/pala/keep", "keep");
    }

    static void fetch(Context c, JSONObject config, SSLContext tls, String path, String pref) throws Exception {
        HttpsURLConnection http = (HttpsURLConnection)new URL("https", config.getString("host"), 8443, path).openConnection();
        http.setSSLSocketFactory(tls.getSocketFactory());
        http.setInstanceFollowRedirects(false);
        http.setRequestProperty("Authorization", "Bearer " + config.getString("token"));
        http.setConnectTimeout(5000);
        http.setReadTimeout(10000);
        try {
            JSONArray items = new JSONArray(new String(bytes(http.getInputStream()), "UTF-8"));
            prefs(c).edit().putString(pref, items.toString()).apply();
        } finally { http.disconnect(); }
    }

    static Map<String, List<String>> list(Context c) {
        Map<String, List<String>> titles = new LinkedHashMap<>();
        Set<String> done = prefs(c).getStringSet("done", Collections.emptySet());
        try {
            JSONArray items = new JSONArray(prefs(c).getString("items", "[]"));
            for (int i = 0; i < items.length(); i++) {
                JSONObject item = items.getJSONObject(i);
                if (!done.contains(item.getString("id"))) {
                    titles.computeIfAbsent(item.getString("title"), k -> new ArrayList<>()).add(item.getString("id"));
                }
            }
        } catch (JSONException ignored) {}
        return titles;
    }

    static void schedule(Context c) {
        JobInfo job = new JobInfo.Builder(57, new ComponentName(c, Sync.class))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setPeriodic(15 * 60 * 1000L).setPersisted(true).build();
        c.getSystemService(JobScheduler.class).schedule(job);
    }
}
