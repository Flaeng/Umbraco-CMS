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

    vm.state = 'loading';
    vm.saveButtonState = "init";
    vm.overrideExistingTranslations = false;
    vm.filesHolder = null;
    vm.overrideCheckboxLabel = 'Override';
    vm.reloadButtonText = 'Reload';

    vm.dataFromFile = null;

    vm.delimiters = [
        { key: ',', text: ',' },
        { key: ';', text: ';' },
        { key: '\t', text: 'tab' },
        { key: '|', text: '|' },
        { key: '^', text: '^' },
        { key: '~', text: '~' }
    ];
    vm.selectedDelimiter = ',';

    vm.encodings = [
        { key: 'ANSI', text: 'ANSI' },
        { key: 'Unicode', text: 'Unicode/UTF-16' },
        { key: 'UTF-8', text: 'UTF-8' },
        { key: 'UTF-32', text: 'UTF-32' },
        { key: 'Windows-1252', text: 'Windows-1252' },
        { key: 'ISO-8859-1', text: 'ISO-8859-1' }
    ];
    vm.selectedEncoding = 'ANSI';

    function onInit() {

        var localizationPromise = localizationService.localizeMany(["importTranslations_overrideExistingCheckboxLabel", "importTranslations_reload"]).then(function (items) {

            var overrideLabel = items[0].value;
            if ((overrideLabel || '').toString().length !== 0) {
                vm.overrideCheckboxLabel = overrideLabel;
            }

            var reloadButton = items[1].value;
            if ((reloadButton || '').toString().length !== 0) {
                vm.reloadButtonText = reloadButton;
            }


        });

        $q.all(localizationPromise).then(function () {
            vm.state = 'upload';
        }, function () {
            vm.state = 'upload';
            localizationService.localize("speechBubbles_dictionaryItemsExportError").then(function (value) {
                notificationsService.error(value);
            });
        });
    }

    vm.import = function (confirmed) {
        vm.saveButtonState = 'busy';

        if (confirmed !== true) {
            vm.loading = true;
        }
        confirmed = confirmed || false;

        dictionaryResource.importDictionaryItems(vm.filesHolder, vm.overrideExistingTranslations, vm.selectedEncoding, vm.selectedDelimiter, confirmed)
            .then(function (data) {

                vm.loading = false;
                vm.saveButtonState = 'init';

                vm.dataFromFile = data;

                if (confirmed) {
                    localizationService.localize("speechBubbles_dictionaryItemsImportedSuccess").then(function (value) {
                        notificationsService.success(value);
                    });

                    navigationService.hideMenu();
                    $window.location.reload(); //This is a temporary fix to make sure the current view is being reloaded
                }

            }, function (evt, status, headers, config) {
                //console.log('evt', evt);

                vm.loading = false;
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
