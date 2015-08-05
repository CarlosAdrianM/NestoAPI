angular.module('EnviosApp', ['ngRoute'])
    .controller('EnviosCtrl', function ($scope, $http, $routeParams) {
        //$scope.answered = false;
        $scope.title = "Cargando datos...";
        //$scope.options = [];
        //$scope.correctAnswer = false;
        $scope.working = false;

        //$scope.answer = function () {
        //    return $scope.correctAnswer ? 'correct' : 'incorrect';
        //};

        $scope.cargarDatos = function ($id, $cliente) {
            $scope.working = true;
            //$scope.answered = false;
            $scope.title = "Cargando envío...";
            $scope.options = [];

            $http.get("/api/EnviosAgencias", {
                params: { id: $id, cliente: $cliente }
            })
            .success(function (data, status, headers, config) {
                $scope.envio = data;
                //$scope.answered = false;
                $scope.working = false;
            }).error(function (data, status, headers, config) {
                $scope.title = "Se ha producido un error al cargar el envío. Pruebe de nuevo más tarde, por favor.";
                $scope.working = false;
            });
        };

        $scope.enviarComentarios = function ($comentario) {
            $http.post('/api/EnviosAgencias', $scope.envio).
              success(function (data, status, headers, config) {
                  alert("Comentario enviado. Muchas gracias.");
              }).
              error(function (data, status, headers, config) {
                  alert("No se ha podido enviar el comentario. Por favor, inténtelo de nuevo.");
              });
        };

        $scope.dobleCiclo = function () {
            if ($scope.envio.Agencia == 4 && $scope.envio.Horario == 1) {
                return true;
            } else {
                return false;
            }
        }



    });