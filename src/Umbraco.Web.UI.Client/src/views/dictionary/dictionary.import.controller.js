/**
 * @ngdoc controller
 * @name Umbraco.Editors.Dictionary.ImportController
 * @function
 * 
 * @description
 * The controller for importing dictionary items
 */
function DictionaryImportController($window, localizationService, navigationService, notificationsService, dictionaryResource, $q) {

    var vm = this; 

    vm.saveButtonState = "init";
    vm.overrideExistingTranslations = false;
    vm.filesHolder = null;
    vm.overrideCheckboxLabel = 'Override';

    function onInit() {

        var localizationPromise = localizationService.localize("importTranslations_overrideExistingCheckboxLabel").then(function (value) {
            if ((value || '').toString().length !== 0) {
                vm.overrideCheckboxLabel = value;
            }
        });

        $q.all(localizationPromise).then(function () {
            vm.loading = false;
        }, function () {
            vm.loading = false;
            localizationService.localize("speechBubbles_dictionaryItemsExportError").then(function (value) {
                notificationsService.error(value);
            });
        });
    }

    vm.import = function () {
        vm.saveButtonState = 'busy';

        dictionaryResource.importDictionaryItems(vm.filesHolder, vm.overrideExistingTranslations)
            .then(function () {

                console.log('reload0');
                localizationService.localize("speechBubbles_dictionaryItemsImportedSuccess").then(function (value) {
                    notificationsService.success(value);
                });

                navigationService.hideMenu();
                $window.location.reload(); //This is a temporary fix to make sure the current view is being reloaded

            }, function (evt, status, headers, config) {

                // set status done
                vm.saveButtonState = "error";

                // If file not found, server will return a 404 and display this message
                if (status === 404) {
                    vm.serverErrorMessage = "File not found";
                }
                else if (status == 400) {
                    //it's a validation error
                    vm.serverErrorMessage = evt.message;
                }
                else {
                    //it's an unhandled error
                    //if the service returns a detailed error
                    if (evt.InnerException) {
                        vm.serverErrorMessage = evt.InnerException.ExceptionMessage;

                        //Check if its the common "too large file" exception
                        if (evt.InnerException.StackTrace && evt.InnerException.StackTrace.indexOf("ValidateRequestEntityLength") > 0) {
                            vm.serverErrorMessage = "File too large to upload";
                        }

                    } else if (evt.Message) {
                        vm.serverErrorMessage = evt.Message;
                    }
                }
                localizationService.localize("speechBubbles_dictionaryItemsImportedError").then(function (value) {
                    notificationsService.error(value, vm.serverErrorMessage);
                });
            });
    }

    vm.close = function () {
        navigationService.hideMenu();
    }

    onInit();

}

angular.module("umbraco").controller("Umbraco.Editors.Dictionary.ImportController", DictionaryImportController);
