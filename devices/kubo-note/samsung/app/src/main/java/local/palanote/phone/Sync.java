package local.palanote.phone;
import android.app.job.*;
public final class Sync extends JobService {
    public boolean onStartJob(JobParameters args) {
        new Thread(() -> {
            try { Store.sync(this); } catch (Exception ignored) { /* Keep the offline list. */ }
            jobFinished(args, false);
        }).start();
        return true;
    }
    public boolean onStopJob(JobParameters args) { return false; }
}
