<template>
    <Dialog v-model:visible="dialogVisible" modal header="Settings" :style="{ width: '40rem' }" @show="onShown"
        @hide="onHide" :dismissableMask="true" :draggable="false">
        <div class="flex align-items-center gap-2 mb-5">
            <label for="theme" class="font-semibold w-10rem">Theme</label>
            
            <Dropdown id="theme" v-model="currentTheme" :options="themes" optionLabel="name"
                placeholder="Select a Theme" class="flex-grow-1" @change="updateTheme" :disabled="isLoading">
                <template #value="slotProps">
                    <div v-if="slotProps.value" class="flex align-items-center">
                        <div class="theme-preview" :style="'background-color: ' + slotProps.value.color"></div>
                        <div>{{ slotProps.value.name }}</div>
                    </div>
                    <span v-else>
                        {{ slotProps.placeholder }}
                    </span>
                </template>
                <template #option="slotProps">
                    <div class="flex align-items-center">
                        <div class="theme-preview" :style="'background-color: ' + slotProps.option.color"></div>
                        <div>{{ slotProps.option.name }}</div>
                    </div>
                </template>
            </Dropdown>

            <Button :icon="isDark ? 'pi pi-sun' : 'pi pi-moon'"
                v-tooltip.right="{ value: 'Toggle Dark Mode', showDelay: 1500 }" text rounded
                aria-label="Toggle Dark Mode" severity="secondary" @click="toggleDarkMode" :disabled="isLoading" />
        </div>

        <div class="flex align-items-center gap-2 mb-5">
            <label for="directory" class="font-semibold w-10rem">Backup Directory</label>
            <InputText id="directory" class="flex-grow-1" placeholder="Backup directory..." v-model="backupLocation"
                @change="directoryChanged" :disabled="isLoading" />
            <Button icon="pi pi-folder-open" text severity="secondary" @click="openDirectory" :disabled="isLoading" />
        </div>

        <div class="flex align-items-center gap-2 mb-5">
            <label for="steamPath" class="font-semibold w-10rem">Steam Path</label>
            <InputText id="steamPath" class="flex-grow-1" placeholder="Steam executable path..." v-model="steamPath"
                @change="hasChanges = true" :disabled="isLoading" />
            <Button icon="pi pi-folder-open" text severity="secondary" @click="openSteamFileDialog"
                :disabled="isLoading" />
        </div>

        <div v-if="supportsScreenCast" class="flex align-items-center gap-2 mb-5">
          <label for="theme" class="font-semibold w-10rem">Screen Capture</label>
  
          <Dropdown id="portal" v-model="currentPortal" :options="portals" optionLabel="name"
                    placeholder="Select a Screen Capture Portal" class="flex-grow-1" :disabled="isLoading" @change="hasChanges = true" />
        </div>
      
        <div class="flex mt-2 gap-2 justify-content-end">
            <Button label="Cancel" severity="secondary" @click="cancel" :disabled="isLoading" />
            <Button label="Save" @click="save" :disabled="!hasChanges || isLoading" :loading="isLoading" />
        </div>
    </Dialog>
</template>

<script setup>
import { ref } from "vue";
import { useToast } from "primevue/usetoast";
import { usePrimeVue } from "primevue/config";

const emit = defineEmits(["shown", "hidden", "saved", "deleted"]);

const toast = useToast();
const primeVue = usePrimeVue();

const dialogVisible = ref(null);
const settings = ref(null);
const hasChanges = ref(false);
const oldIsDark = ref(true);
const isDark = ref(true);
const oldTheme = ref({ name: "Indigo", color: "#818cf8" });
const currentTheme = ref({ name: "Indigo", color: "#818cf8" });
const themes = ref([
    { name: "Amber", color: "#fbbf24" },
    { name: "Blue", color: "#60a5fa" },
    { name: "Cyan", color: "#22d3ee" },
    { name: "Green", color: "#34d399" },
    { name: "Indigo", color: "#818cf8" },
    { name: "Lime", color: "#a3e635" },
    { name: "Noir", color: "#fafafa" },
    { name: "Pink", color: "#f472b6" },
    { name: "Purple", color: "#a78bfa" },
    { name: "Teal", color: "#2dd4bf" },
]);
const backupLocation = ref("");
const steamPath = ref("");
const isLoading = ref(false);
const shouldReset = ref(true);
const supportsScreenCast = ref(false);

const currentPortal = ref({ name: "Screenshot", value: 0 });
const portals = ref([
  { name: "Screenshot", value: 0 },
  { name: "ScreenCast", value: 1 },
]);

initialize();

const show = showDialog;
defineExpose({
    show
});

function initialize() {
    galdrInvoke("getSettings")
        .then(x => {
            settings.value = x;
            currentTheme.value = themes.value.find(theme => theme.name == settings.value.theme);
            isDark.value = settings.value.isDark;
            updateTheme();
            oldIsDark.value = isDark.value;
            supportsScreenCast.value = settings.value.supportsScreenCast;
            if (supportsScreenCast.value) {
                currentPortal.value = portals.value.find(portal => portal.value == settings.value.screenCapturePortal);
            }
        });
}

function toggleDarkMode() {
    hasChanges.value = true;

    isDark.value = !isDark.value;
    updateTheme();
    oldIsDark.value = isDark.value;
}

function updateTheme() {
    hasChanges.value = true;

    var oldThemeName = "aura-" + (oldIsDark.value ? "dark-" : "light-") + oldTheme.value.name.toLowerCase();
    var newThemeName = "aura-" + (isDark.value ? "dark-" : "light-") + currentTheme.value.name.toLowerCase();
    primeVue.changeTheme(oldThemeName, newThemeName, "theme-link", () => { });
    oldTheme.value = currentTheme.value;
}

function showDialog() {
    shouldReset.value = true;
    getSettings();
}

function onShown() {
    emit("shown");
}

function onHide() {
    if (shouldReset.value) {
        cancel();
    }
    hasChanges.value = false;
    emit("hidden");
}

function getSettings() {
    galdrInvoke("getSettings")
        .then(x => {
            settings.value = x;

            currentTheme.value = themes.value.find(theme => theme.name == settings.value.theme);
            isDark.value = settings.value.isDark;
            backupLocation.value = settings.value.backupsDirectory;
            steamPath.value = settings.value.steamExePath;

            supportsScreenCast.value = settings.value.supportsScreenCast;
            if (supportsScreenCast.value) {
                currentPortal.value = portals.value.find(portal => portal.value == settings.value.screenCapturePortal);
            }
              
            dialogVisible.value = true;
        })
        .catch(e => {
            console.error(e);
            toast.add({ severity: 'error', summary: 'Failed', detail: 'Failed to load settings', group: 'tr', life: 3000 });
        });
}

function directoryChanged() {
    hasChanges.value = true;
}

async function openDirectory() {
    const directoryResult = await galdrInvoke("openDirectoryDialog");
    if (directoryResult && directoryResult.directory) {
        hasChanges.value = backupLocation.value != directoryResult.directory;
        backupLocation.value = directoryResult.directory;
    }
}

async function openSteamFileDialog() {
    const fileResult = await galdrInvoke("openFileDialog");
    if (fileResult && fileResult.file) {
        hasChanges.value = steamPath != fileResult.file;
        steamPath.value = fileResult.file;
    }
}

async function cancel() {
    if (settings.value) {
        currentTheme.value = themes.value.find(theme => theme.name == settings.value.theme);
        isDark.value = settings.value.isDark;
        updateTheme();
        oldIsDark.value = isDark.value;

        if (supportsScreenCast.value) {
          currentPortal.value = portals.value.find(portal => portal.value == settings.value.screenCapturePortal);
        }

        dialogVisible.value = false;
        settings.value = null;
    }
}

async function save() {
    isLoading.value = true;

    const saveResult = await galdrInvoke("saveSettings", {
        settings: {
            theme: currentTheme.value.name,
            isDark: isDark.value,
            backupsDirectory: backupLocation.value,
            steamExePath: steamPath.value,
            screenCapturePortal: currentPortal.value.value,
        }
    });

    isLoading.value = false;

    if (saveResult.saved) {
        toast.add({ severity: 'success', summary: 'Success', detail: 'Settings saved successfully', group: 'tr', life: 3000 });
        shouldReset.value = false;
        dialogVisible.value = false;
    } else {
        toast.add({ severity: 'error', summary: 'Failed', detail: 'Failed to save settings', group: 'tr', life: 3000 });
    }
}
</script>

<style scoped>
.theme-preview {
    width: 1rem;
    height: 1rem;
    border-radius: 1rem;
    margin-right: 0.5rem;
    border: 1px solid gray;
}
</style>
