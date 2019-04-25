/**
 * @ngdoc controller
 * @name Umbraco.Editors.Dictionary.ExportController
 * @function
 * 
 * @description
 * The controller for exporting dictionary items
 */
function DictionaryExportController($q, languageResource, dictionaryResource, navigationService, notificationsService, localizationService) {

    var vm = this;

    vm.loading = true;
    vm.languages = [];
    vm.allLanguagesAreSelected = false;
    vm.saveButtonState = 'init';
    vm.selectAllText = 'Select all';

    function onInit() {

        var localizationPromise = localizationService.localize("exportTranslations_selectAll").then(function (value) {
            if ((value || '').toString().length !== 0) {
                vm.selectAllText = value;
            }
        });

        var getAllLanguagesPromise = languageResource.getAll()
            .then(function (data) {
                vm.languages = data;
            });

        //When all languages and translations (for the view) has been loaded the view can be displayed
        $q.all(localizationPromise, getAllLanguagesPromise).then(function () {
            vm.loading = false;
        }, function () {
            vm.loading = false;
            localizationService.localize("speechBubbles_dictionaryItemsExportError").then(function (value) {
                notificationsService.error(value);
            });
        });
    }

    vm.toggleAllLanguagesAreSelectedIfLangIsSelected = function (lang) {
        if (lang.selected === false) {
            vm.allLanguagesAreSelected = false;
        }
    }

    vm.toggleAllLanguages = function () {
        for (var i = 0; i < vm.languages.length; i++) {
            vm.languages[i].selected = vm.allLanguagesAreSelected;
        }
    }

    vm.export = function () {
        vm.saveButtonState = "busy";
        var selectedLanguageIds = [];
        for (var i = 0; i < vm.languages.length; i++) {
            var lang = vm.languages[i];
            if (lang.selected === true) {
                selectedLanguageIds.push(lang.id);
            }
        }

        if (selectedLanguageIds.length === 0) {
            return;
        }

        dictionaryResource.exportDictionaryItems(selectedLanguageIds).then(function () {
            vm.saveButtonState = "success";
            navigationService.hideMenu();
            localizationService.localize("speechBubbles_dictionaryItemsExportedSuccess").then(function (value) {
                notificationsService.success(value);
            });
        }, function () {
                vm.saveButtonState = "error";
                localizationService.localize("speechBubbles_dictionaryItemsExportedError").then(function (value) {
                    notificationsService.error(value);
                });
        });
    }

    vm.close = function () {
        navigationService.hideMenu();
    }

    onInit();

}

angular.module("umbraco").controller("Umbraco.Editors.Dictionary.ExportController", DictionaryExportController);
