package local.palanote.phone;
import android.app.*;
import android.content.*;
import android.os.Build;
import android.location.LocationManager;
import java.util.*;
import org.json.*;

public final class Arrival extends BroadcastReceiver {
    static void register(Context c) throws JSONException, SecurityException {
        JSONObject cfg = Store.config(c);
        PendingIntent target = PendingIntent.getBroadcast(c, 11, new Intent(c, Arrival.class),
            PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0));
        c.getSystemService(LocationManager.class).addProximityAlert(cfg.getDouble("lat"), cfg.getDouble("lon"), 150f, -1, target);
        Store.prefs(c).edit().putBoolean("arrival", true).apply();
    }

    public void onReceive(Context c, Intent intent) {
        if (Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())) {
            if (Store.prefs(c).getBoolean("arrival", false)) {
                try { register(c); } catch (Exception ignored) {}
            }
            return;
        }
        if (!intent.getBooleanExtra(LocationManager.KEY_PROXIMITY_ENTERING, false)) return;
        String items = String.join(", ", Store.list(c).keySet());
        if (items.isEmpty()) return;
        NotificationManager manager = c.getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel("shop", "Príchod do predajne", NotificationManager.IMPORTANCE_DEFAULT));
        PendingIntent open = PendingIntent.getActivity(c, 12, new Intent(c, Shopping.class), PendingIntent.FLAG_IMMUTABLE);
        manager.notify(12, new Notification.Builder(c, "shop").setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle("Nákupný zoznam").setContentText(items).setStyle(new Notification.BigTextStyle().bigText(items))
            .setContentIntent(open).setAutoCancel(true).build());
    }
}
