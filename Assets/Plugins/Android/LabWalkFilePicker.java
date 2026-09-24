package com.architecturelab.labwalk;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;

/**
 * Opens the Android system document picker and copies the chosen file into app storage.
 * Unity never sees a content:// URI: it polls resultPath for a small JSON status file
 * written after the copy finishes (status: copied | cancelled | error).
 * Uses a headless framework Fragment so no manifest activity entry is needed.
 */
public final class LabWalkFilePicker extends Fragment {
    private static final String TAG = "LabWalkFilePicker";
    private static final int REQUEST = 4231;
    private static final long MAX_BYTES = 150L * 1024 * 1024;
    private String destinationFolder, resultPath;

    /** Called from Unity's thread; the picker starts on the UI thread. Failures arrive as an error result. */
    public static void open(final Activity activity, final String destinationFolder, final String resultPath) {
        activity.runOnUiThread(() -> {
            try {
                FragmentManager fm = activity.getFragmentManager();
                Fragment old = fm.findFragmentByTag(TAG);
                if (old != null) fm.beginTransaction().remove(old).commitNowAllowingStateLoss();
                LabWalkFilePicker picker = new LabWalkFilePicker();
                picker.destinationFolder = destinationFolder;
                picker.resultPath = resultPath;
                fm.beginTransaction().add(picker, TAG).commitNowAllowingStateLoss();
                Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*"); // .3dm has no registered MIME type; the app validates the file itself.
                picker.startActivityForResult(intent, REQUEST);
            } catch (Exception e) {
                write(resultPath, "error", "", "System file picker unavailable: " + e.getMessage());
            }
        });
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode != REQUEST) return;
        final Activity activity = getActivity();
        final Uri uri = resultCode == Activity.RESULT_OK && data != null ? data.getData() : null;
        final String folder = destinationFolder, result = resultPath;
        if (activity != null) activity.getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
        if (uri == null || activity == null) { write(result, "cancelled", "", ""); return; }
        new Thread(() -> copy(activity, uri, folder, result), "LabWalkImportCopy").start();
    }

    private static void copy(Activity activity, Uri uri, String folder, String result) {
        String name = "picked";
        try {
            long size = -1;
            try (Cursor c = activity.getContentResolver().query(uri, null, null, null, null)) {
                if (c != null && c.moveToFirst()) {
                    int n = c.getColumnIndex(OpenableColumns.DISPLAY_NAME), s = c.getColumnIndex(OpenableColumns.SIZE);
                    if (n >= 0 && !c.isNull(n)) name = c.getString(n);
                    if (s >= 0 && !c.isNull(s)) size = c.getLong(s);
                }
            }
            name = new File(name).getName().replaceAll("[\\\\/:*?\"<>|]", "_");
            if (size > MAX_BYTES) { write(result, "error", name, "File is larger than 150 MB."); return; }
            File dir = new File(folder);
            if (!dir.isDirectory() && !dir.mkdirs()) throw new Exception("Cannot create " + folder);
            File target = new File(dir, name), temp = new File(dir, name + ".part");
            long total = 0;
            try (InputStream in = activity.getContentResolver().openInputStream(uri); OutputStream out = new FileOutputStream(temp)) {
                if (in == null) throw new Exception("The file could not be opened.");
                byte[] buffer = new byte[1 << 16];
                for (int read; (read = in.read(buffer)) > 0; ) {
                    total += read;
                    if (total > MAX_BYTES) throw new Exception("File is larger than 150 MB.");
                    out.write(buffer, 0, read);
                }
            } catch (Exception e) { temp.delete(); throw e; }
            if (target.exists() && !target.delete()) throw new Exception("Cannot replace " + target);
            if (!temp.renameTo(target)) throw new Exception("Cannot finish copying " + name);
            write(result, "copied", target.getAbsolutePath(), "");
        } catch (Exception e) {
            write(result, "error", name, String.valueOf(e.getMessage()));
        }
    }

    private static void write(String path, String status, String file, String error) {
        String json = "{\"status\":\"" + esc(status) + "\",\"file\":\"" + esc(file) + "\",\"error\":\"" + esc(error) + "\"}";
        try {
            File target = new File(path), temp = new File(path + ".tmp");
            File parent = target.getParentFile();
            if (parent != null) parent.mkdirs();
            try (OutputStream out = new FileOutputStream(temp)) { out.write(json.getBytes(StandardCharsets.UTF_8)); }
            target.delete();
            temp.renameTo(target);
        } catch (Exception ignored) { }
    }

    private static String esc(String s) {
        return s == null ? "" : s.replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", " ");
    }
}
