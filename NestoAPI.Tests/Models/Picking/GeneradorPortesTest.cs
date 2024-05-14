using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    
    public class GeneradorPortesTest
    {
        // NO SE PUEDEN HACER TESTS AHORA MISMO POR ESTA LÍNEA (HABRIA QUE INYECTAR EL SERVICIO):
        // GestorPedidosVenta gestorPedidos = new GestorPedidosVenta(new ServicioPedidosVenta());

        /*
        [TestMethod]
        public void GeneradorPortes_LaFormaDePagoEsEfectivo_SumaPortesContrareembolso()
        {
            // Arrange
            var db = A.Fake<NVEntities>();
            var rellenadorPrepagosService = A.Fake<IRellenadorPrepagosService>();
            var pedido = new PedidoPicking(rellenadorPrepagosService)
            {
                CodigoPostal = "28000"
            };
            var generadorPortes = new GeneradorPortes(db, pedido);

            // Act
            generadorPortes.Ejecutar();

            // Assert
            pedido.Lineas.Last().BaseImponible = 3M;
        }
        */
    }
}
