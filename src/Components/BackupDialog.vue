<template>
    <Dialog v-model:visible="dialogVisible" modal header="Edit Backup" :style="{ width: '28rem' }" @show="onShown"
        @hide="onHide" :dismissableMask="true" :draggable="false">
        <div class="flex align-items-center gap-3 mb-4">
            <label for="time" class="font-semibold w-6rem">Time</label>
            <span id="time" class="p-text-secondary ml-1">{{ new Date(selectedBackup.time).toLocaleString() }}</span>
        </div>

        <div class="flex align-items-center gap-3 mb-3">
            <label for="tag" class="font-semibold w-6rem">Tag</label>
            <InputText id="tag" class="flex-auto" autocomplete="off" v-model="tag" />
        </div>

        <div class="flex align-items-center gap-2 mb-5">
            <label for="favorite" class="font-semibold w-6rem">Favorite</label>
            <Button :icon="isFavorite ? 'pi pi-star-fill' : 'pi pi-star'" :plain="!isFavorite" text
                @click="isFavorite = !isFavorite" v-tooltip.right="{ value: 'Prevents Deletion', showDelay: 1500 }" />
        </div>

        <div class="flex gap-2">
            <div class="flex-grow-1">
                <Button type="button" label="Delete" severity="danger" @click="deleteBackup"></Button>
            </div>

            <div class="flex gap-2">
                <Button type="button" label="Cancel" severity="secondary" @click="dialogVisible = false"></Button>
                <Button type="button" label="Save" @click="save"></Button>
            </div>
        </div>

        <ConfirmPopup group="popup"></ConfirmPopup>
    </Dialog>
</template>

<script setup>
import { ref } from "vue";
import { useConfirm } from "primevue/useconfirm";
import { useToast } from "primevue/usetoast";

const props = defineProps(["selectedBackup"]);
const emit = defineEmits(["shown", "hidden", "saved", "deleted"]);

const confirm = useConfirm();
const toast = useToast();

const dialogVisible = ref(null);
const tag = ref(null);
const isFavorite = ref(false);

const show = showDialog;
defineExpose({
    show
});

function showDialog(backup) {
    tag.value = backup?.tag;
    isFavorite.value = backup?.isFavorite;
    dialogVisible.value = true;
}

function onShown() {
    emit("shown");
}

function onHide() {
    emit("hidden");
}

function deleteBackup(event) {
    confirm.require({
        target: event.currentTarget,
        message: 'Do you want to delete this backup?',
        icon: 'pi pi-info-circle',
        rejectClass: 'p-button-secondary p-button-outlined p-button-sm',
        acceptClass: 'p-button-danger p-button-sm',
        rejectLabel: 'Cancel',
        acceptLabel: 'Delete',
        group: 'popup',
        accept: async () => {
            const deleteResult = await galdrInvoke("deleteBackup", {
                backup: {
                    saveId: props.selectedBackup.saveId,
                    directory: props.selectedBackup.directory,
                }
            });

            if (deleteResult && deleteResult.success) {
                emit("deleted", props.selectedBackup.directory);
                dialogVisible.value = false;
                toast.add({ severity: 'success', summary: 'Success', detail: 'Backup deleted', group: 'tr', life: 3000 });
            } else {
                toast.add({ severity: 'error', summary: 'Failed', detail: 'Failed to delete backup', group: 'tr', life: 3000 });
            }
        },
        reject: () => {

        }
    });
}

async function save() {
    const saved = await galdrInvoke("updateMetadata", {
        backup: {
            saveId: props.selectedBackup.saveId,
            directory: props.selectedBackup.directory,
            tag: tag.value,
            isFavorite: isFavorite.value
        }
    });

    if (saved) {
        dialogVisible.value = false;
        toast.add({ severity: 'success', summary: 'Success', detail: 'Saved successfully', group: 'tr', life: 3000 });
        emit("saved", tag.value, isFavorite.value);
    } else {
        toast.add({ severity: 'error', summary: 'Failed', detail: 'Saved failed', group: 'tr', life: 3000 });
    }
}
</script>

<style scoped></style>

<style lang="scss" scoped></style>
