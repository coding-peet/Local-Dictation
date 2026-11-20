package local.palanote.phone;

import android.Manifest;
import android.app.Activity;
import android.content.*;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.view.*;
import android.widget.*;
import java.util.*;
import org.json.*;

public final class Shopping extends Activity {
    LinearLayout panel;
    TextView status;
    String message = "";

    public void onCreate(Bundle b) {
        super.onCreate(b);
        setTitle("Pala Nákup");
        if (Build.VERSION.SDK_INT >= 30) getWindow().setDecorFitsSystemWindows(false);
        draw();
    }
    public void onResume() { super.onResume(); draw(); if (Store.prefs(this).contains("pair")) refresh(); }

    void button(String label, Runnable action) {
        Button b = new Button(this); b.setText(label); b.setOnClickListener(v -> action.run()); panel.addView(b);
    }
    void draw() {
        ScrollView scroll = new ScrollView(this);
        if (Build.VERSION.SDK_INT >= 30) {
            scroll.setOnApplyWindowInsetsListener((view, insets) -> {
                android.graphics.Insets safe = insets.getInsets(
                    WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(safe.left, safe.top, safe.right, safe.bottom);
                return insets;
            });
        }
        panel = new LinearLayout(this); panel.setPadding(24, 24, 24, 24); panel.setOrientation(LinearLayout.VERTICAL);
        scroll.addView(panel); setContentView(scroll);
        TextView title = new TextView(this);
        title.setText("Pala Nákup");
        title.setTextAppearance(android.R.style.TextAppearance_Material_Title);
        panel.addView(title);
        status = new TextView(this); status.setText(message); panel.addView(status);
        if (Store.prefs(this).contains("pair")) {
            for (Map.Entry<String,List<String>> item : Store.list(this).entrySet()) {
                CheckBox row = new CheckBox(this); row.setText(item.getKey()); panel.addView(row);
                row.setOnClickListener(v -> {
                    Set<String> done = new HashSet<>(Store.prefs(this).getStringSet("done", Collections.emptySet()));
                    done.addAll(item.getValue()); Store.prefs(this).edit().putStringSet("done", done).apply(); draw();
                });
            }
            button("Synchronizovať s PC", this::refresh);
            button("Uložiť kópiu do Google Keep", () -> {
                String text = String.join("\n", Store.list(this).keySet());
                if (text.isEmpty()) return;
                share("Nákup", text);
            });
            try {
                JSONArray notes = new JSONArray(Store.prefs(this).getString("keep", "[]"));
                for (int i = notes.length() - 1; i >= 0; i--) {
                    String text = notes.getJSONObject(i).getString("title");
                    String excerpt = text.substring(0, Math.min(48, text.length()));
                    button("Do Keep: " + excerpt, () -> share("Pala Note", text));
                }
            } catch (JSONException ignored) {}
            button("Zapnúť upozornenie pri predajni", this::geofence);
        }
        button("Spárovať súbor z PC", () -> startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT)
            .addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"), 4));
    }
    void share(String title, String text) {
        Intent send = new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_SUBJECT,title)
                    .putExtra(Intent.EXTRA_TEXT,text).setPackage("com.google.android.keep");
                try { startActivity(send); } catch (ActivityNotFoundException e) { status.setText("Nainštaluj Google Keep."); }
    }

    void refresh() {
        new Thread(() -> {
            try { Store.sync(this); message = "Zoznam je aktuálny."; }
            catch(Exception e) { message = "PC nedostupný. Zobrazený je posledný stiahnutý zoznam."; }
            runOnUiThread(this::draw);
        }).start();
    }
    void geofence() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 3); return;
        }
        if (checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.ACCESS_COARSE_LOCATION, Manifest.permission.ACCESS_FINE_LOCATION}, 3); return;
        }
        if (checkSelfPermission(Manifest.permission.ACCESS_BACKGROUND_LOCATION) != PackageManager.PERMISSION_GRANTED) {
            Toast.makeText(this, "Povoľ presnú polohu vždy a znova stlač tlačidlo.", Toast.LENGTH_LONG).show();
            startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName()))); return;
        }
        try { Arrival.register(this); status.setText("Predajňa je nastavená."); }
        catch(Exception e) { status.setText("Skontroluj párovanie a polohu."); }
    }
    public void onRequestPermissionsResult(int code, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(code, permissions, results);
        if(code == 3 && results.length > 0 && results[results.length - 1] == PackageManager.PERMISSION_GRANTED) geofence();
    }
    protected void onActivityResult(int code, int result, Intent data) {
        super.onActivityResult(code, result, data);
        if (code != 4 || result != RESULT_OK || data == null) return;
        try {
            JSONObject cfg = new JSONObject(new String(Store.bytes(getContentResolver().openInputStream(data.getData())), "UTF-8"));
            cfg.getString("host"); cfg.getString("token"); cfg.getString("cert"); cfg.getDouble("lat"); cfg.getDouble("lon");
            Store.prefs(this).edit().putString("pair", cfg.toString()).remove("items").remove("keep").apply();
            Store.schedule(this); refresh();
        } catch(Exception e) { status.setText("Neplatný párovací súbor."); }
    }
}
