import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import fs from "fs";
import path from "path";
import { minify } from "csso";

// Plugin to minify CSS files in dist/themes/ after build
function minifyThemesPlugin() {
    return {
        name: 'minify-themes',
        closeBundle() {
            const distThemesDir = path.resolve('dist/themes');

            if (!fs.existsSync(distThemesDir)) {
                console.log('Warning: No themes directory in dist, skipping minification');
                return;
            }

            console.log('\nMinifying theme CSS files...');

            let totalOriginalSize = 0;
            let totalMinifiedSize = 0;
            let filesProcessed = 0;

            // Find all theme.css files recursively
            function findThemeFiles(dir) {
                const files = fs.readdirSync(dir);
                const themeFiles = [];

                for (const file of files) {
                    const fullPath = path.join(dir, file);
                    const stat = fs.statSync(fullPath);

                    if (stat.isDirectory()) {
                        themeFiles.push(...findThemeFiles(fullPath));
                    } else if (file === 'theme.css') {
                        themeFiles.push(fullPath);
                    }
                }

                return themeFiles;
            }

            const themeFiles = findThemeFiles(distThemesDir);

            for (const themeFile of themeFiles) {
                try {
                    const originalCss = fs.readFileSync(themeFile, 'utf8');
                    const originalSize = originalCss.length;

                    const minified = minify(originalCss);
                    const minifiedSize = minified.css.length;

                    fs.writeFileSync(themeFile, minified.css);

                    totalOriginalSize += originalSize;
                    totalMinifiedSize += minifiedSize;
                    filesProcessed++;

                    const savedKB = ((originalSize - minifiedSize) / 1024).toFixed(2);
                    const savedPercent = (((originalSize - minifiedSize) / originalSize) * 100).toFixed(1);
                    const fileName = path.relative('dist/themes', themeFile);

                    console.log(`  [OK] ${fileName}: ${(originalSize / 1024).toFixed(2)} KB -> ${(minifiedSize / 1024).toFixed(2)} KB (${savedPercent}% smaller)`);
                } catch (error) {
                    console.error(`  [ERROR] Failed to minify ${themeFile}:`, error.message);
                }
            }

            const totalSavedMB = ((totalOriginalSize - totalMinifiedSize) / 1024 / 1024).toFixed(2);
            const totalPercent = (((totalOriginalSize - totalMinifiedSize) / totalOriginalSize) * 100).toFixed(1);

            console.log(`\n  Summary: Minified ${filesProcessed} files, saved ${totalSavedMB} MB (${totalPercent}% reduction)\n`);
        }
    };
}

// https://vitejs.dev/config/
export default defineConfig(async () => ({
    plugins: [vue(), minifyThemesPlugin()],
    clearScreen: false,
    server: {
        port: 1314,
        strictPort: true,
    },
}));
