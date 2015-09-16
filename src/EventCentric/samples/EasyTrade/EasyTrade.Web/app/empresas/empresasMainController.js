﻿(function () {
    'use strict';

    angular
        .module('app')
        .controller('empresasMainController', empresasMainController);

    empresasMainController.$inject = ['empresasMessageSender', 'empresasDao', '$state', 'utils'];

    function empresasMainController(empresasMessageSender, empresasDao, $state, utils) {
        var vm = this;
        var dao = empresasDao;
        var sender = empresasMessageSender;

        // View models
        vm.busy = false;
        vm.busyMessage = '';
        vm.empresas = [];
        vm.mostrarTodo = false;
        vm.mostrarTodoLabel;
        vm.mostrarTodo = true;

        // Commands
        vm.alternarMostrarTodo = alternarMostrarTodo;
        vm.desactivarEmpresa = desactivarEmpresa;
        vm.reactivarEmpresa = reactivarEmpresa;
        
        // Redirects
        vm.redirectToNuevaEmpresa = redirectToNuevaEmpresa;
        vm.redirectToActualizarEmpresa = redirectToActualizarEmpresa;

        activate();

        function activate() {
            //...
            obtenerTodasLasEmpresas();
            alternarMostrarTodo();
        }

        function obtenerTodasLasEmpresas() {
            enterBusy('Recuperando lista de empresas...');
            //enterBusy('');
            dao.obtenerTodasLasEmpresas()
                .then(function (data) {
                    vm.empresas = data;
                    exitBusy();
                },
                function (message) {
                    toastr.error(message.data.exceptionMessage);
                    exitBusy();
                });
        }

        function desactivarEmpresa(idEmpresa, nombre) {
            enterBusy('Desactivando empresa ' + nombre + '...');
            //enterBusy('');
            sender.desactivarEmpresa(idEmpresa)
                .then(function (data) {
                    // await result
                    dao.awaitResult(data.data)
                        .then(function (data) {
                            // empresa desactivada
                            toastr.info(data.message);

                            // volvemos a obtener todas las empresas
                            obtenerTodasLasEmpresas();
                        },
                        function (message) {
                            toastr.error(message.data.exceptionMessage);
                            exitBusy();
                        })
                },
                function (message) {
                    toastr.error(message.data.exceptionMessage);
                    exitBusy();
                });
        }

        function reactivarEmpresa(idEmpresa, nombre) {
            enterBusy('Reactivando empresa ' + nombre + '...');
            //enterBusy('');
            sender.reactivarEmpresa(idEmpresa)
                .then(function (data) {
                    // await result
                    dao.awaitResult(data.data)
                        .then(function (data) {
                            // empresa desactivada
                            toastr.info(data.message);

                            // volvemos a obtener todas las empresas
                            obtenerTodasLasEmpresas();
                        },
                        function (message) {
                            toast.empresas(message.data.exceptionMessage);
                            exitBusy();
                        })
                },
                function (message) {
                    toastr.error(message.data.exceptionMessage);
                    exitBusy();
                });
        }

        function alternarMostrarTodo() {
            vm.mostrarTodo = !vm.mostrarTodo;

            if (vm.mostrarTodo)
                vm.mostrarTodoLabel = 'Ocultar inactivas';
            else
                vm.mostrarTodoLabel = 'Mostrar todo'
        }

        function redirectToNuevaEmpresa() {
            utils.animateTransitionTo('section.main', 'fadeInLeft', 'fadeOutRight', function () {
                $state.go('nuevaEmpresa');
            });
        }

        function redirectToActualizarEmpresa(idEmpresa) {
            utils.animateTransitionTo('section.main', 'fadeInLeft', 'fadeOutRight', function () {
                window.location = '#/actualizar-empresa?idEmpresa=' + idEmpresa;
            });
        }
        
        function enterBusy(message) {
            vm.busy = true;
            vm.busyMessage = message;

            utils.animateTransitionTo('.busy', 'fadeIn', 'fadeIn', function () {
                //
            });
        }

        function exitBusy() {
            vm.busy = false;
        }
    }
})();
