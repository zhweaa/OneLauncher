import java.nio.file.*;
public class WorkingDirectoryProbe {
    public static void main(String[] args) throws Exception {
        Path gameDir = null, assetsDir = null;
        for (int i = 0; i < args.length - 1; i++) {
            if (args[i].equals("--gameDir")) gameDir = Path.of(args[i + 1]);
            if (args[i].equals("--assetsDir")) assetsDir = Path.of(args[i + 1]);
        }
        if (!Path.of("").toRealPath().equals(gameDir.toRealPath())) throw new Exception("user.dir differs from --gameDir");
        if (!Files.exists(assetsDir.resolve("shared.marker"))) throw new Exception("shared assets not rooted correctly");
        String value = Files.readString(Path.of("config/fancymenu/customizablemenus.txt"));
        Files.writeString(Path.of("probe.result"), value);
        System.out.println("PROBE_OK " + value);
    }
}