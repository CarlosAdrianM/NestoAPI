using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using M = NestoAPI.Models.Constantes.Pedidos.ModosFacturacion;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#542 (corte 1): la única regla de negocio sobre el modo de facturación es la de los plazos, que
    /// hoy viven en los triggers trgCabPedidoVtaIns/Upd: plazos que no son de la ficha (salvo contado, CR y fin
    /// de mes) obligan a facturar de una vez. Con modos, eso prohíbe solo «por entregas».
    /// </summary>
    [TestClass]
    public class ModosFacturacionPermitidosTests
    {
        [TestMethod]
        public void PlazosExigenUnaSolaFactura_LaCondicionLiteralDelTrigger()
        {
            Assert.IsTrue(ModosFacturacionPermitidos.PlazosExigenUnaSolaFactura("30/60", "NRM", plazosSonDeLaFicha: false));
            Assert.IsFalse(ModosFacturacionPermitidos.PlazosExigenUnaSolaFactura("30/60", "NRM", plazosSonDeLaFicha: true));
            Assert.IsFalse(ModosFacturacionPermitidos.PlazosExigenUnaSolaFactura("CONTADO", "NRM", plazosSonDeLaFicha: false));
            Assert.IsFalse(ModosFacturacionPermitidos.PlazosExigenUnaSolaFactura("CR", "NRM", plazosSonDeLaFicha: false));
            Assert.IsFalse(ModosFacturacionPermitidos.PlazosExigenUnaSolaFactura("30/60", "FDM", plazosSonDeLaFicha: false));
            // Los char de la BD llegan con relleno
            Assert.IsFalse(ModosFacturacionPermitidos.PlazosExigenUnaSolaFactura("CONTADO   ", "NRM", plazosSonDeLaFicha: false));
        }

        [TestMethod]
        public void Calcular_PlazosDeLaFicha_TodosPermitidos()
        {
            List<ModoFacturacionPermitidoDTO> modos = ModosFacturacionPermitidos.Calcular(unaSolaFactura: false, notaEntrega: false);

            CollectionAssert.AreEqual(new List<byte> { 1, 2, 3 }, ModosFacturacionPermitidos.Permitidos(modos));
            Assert.IsTrue(modos.All(m => m.Motivo == null));
        }

        [TestMethod]
        public void Calcular_PlazosQueExigenUnaSolaFactura_SoloSeProhibePorEntregas()
        {
            List<ModoFacturacionPermitidoDTO> modos = ModosFacturacionPermitidos.Calcular(unaSolaFactura: true, notaEntrega: false);

            CollectionAssert.AreEqual(new List<byte> { M.AL_COMPLETAR, M.TODO_AHORA_Y_LO_PENDIENTE_DESPUES }, ModosFacturacionPermitidos.Permitidos(modos));
            Assert.AreEqual(ModosFacturacionPermitidos.MOTIVO_PLAZOS, modos.Single(m => m.Modo == M.POR_ENTREGAS).Motivo);
        }

        [TestMethod]
        public void Calcular_NotaDeEntrega_NingunModo()
        {
            List<ModoFacturacionPermitidoDTO> modos = ModosFacturacionPermitidos.Calcular(unaSolaFactura: false, notaEntrega: true);

            Assert.AreEqual(0, ModosFacturacionPermitidos.Permitidos(modos).Count);
        }

        [TestMethod]
        public void Sugerir_ElModoActualSiSePuede_SiNoAlCompletar()
        {
            var porEntregas = new PedidoVentaDTO { plazosPago = "30/60", periodoFacturacion = "NRM", mantenerJunto = false };

            ModosFacturacionPermitidos.Sugerencia conFicha = ModosFacturacionPermitidos.Sugerir(porEntregas, null, plazosSonDeLaFicha: true);
            ModosFacturacionPermitidos.Sugerencia sinFicha = ModosFacturacionPermitidos.Sugerir(porEntregas, null, plazosSonDeLaFicha: false);

            Assert.AreEqual(M.POR_ENTREGAS, conFicha.Modo);
            Assert.IsNull(conFicha.Motivo);
            Assert.AreEqual(M.AL_COMPLETAR, sinFicha.Modo, "Es lo que hacía el trigger con esos plazos");
            Assert.AreEqual(ModosFacturacionPermitidos.MOTIVO_PLAZOS, sinFicha.Motivo);
        }

        [TestMethod]
        public void Sugerir_SinModoEnElDto_RespetaElTresGuardado()
        {
            var pedido = new PedidoVentaDTO { plazosPago = "30/60", periodoFacturacion = "NRM", mantenerJunto = false };

            ModosFacturacionPermitidos.Sugerencia sugerencia = ModosFacturacionPermitidos.Sugerir(pedido, modoAlmacenado: 3, plazosSonDeLaFicha: false);

            Assert.AreEqual(M.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, sugerencia.Modo, "El 3 factura de una vez: vale con plazos especiales");
        }

        [TestMethod]
        public void MotivoRechazo_SoloPorEntregasConPlazosEspeciales()
        {
            Assert.IsNotNull(ModosFacturacionPermitidos.MotivoRechazo(M.POR_ENTREGAS, "30/60", "NRM", plazosSonDeLaFicha: false, notaEntrega: false));
            Assert.IsNull(ModosFacturacionPermitidos.MotivoRechazo(M.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, "30/60", "NRM", plazosSonDeLaFicha: false, notaEntrega: false));
            Assert.IsNull(ModosFacturacionPermitidos.MotivoRechazo(M.POR_ENTREGAS, "30/60", "NRM", plazosSonDeLaFicha: true, notaEntrega: false));
        }

        // El controlador: la pregunta a CondPagoClientes es la misma que hacen los triggers (contacto de cobro)

        private static NVEntities DbConCondiciones(params CondPagoCliente[] condiciones)
        {
            NVEntities db = A.Fake<NVEntities>();
            var datos = condiciones.AsQueryable();
            DbSet<CondPagoCliente> fake = A.Fake<DbSet<CondPagoCliente>>(o => o.Implements<IQueryable<CondPagoCliente>>());
            A.CallTo(() => ((IQueryable<CondPagoCliente>)fake).Provider).Returns(datos.Provider);
            A.CallTo(() => ((IQueryable<CondPagoCliente>)fake).Expression).Returns(datos.Expression);
            A.CallTo(() => ((IQueryable<CondPagoCliente>)fake).ElementType).Returns(datos.ElementType);
            A.CallTo(() => ((IQueryable<CondPagoCliente>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => db.CondPagoClientes).Returns(fake);
            return db;
        }

        [TestMethod]
        public void NormalizarModoFacturacion_ModoInformadoQueNoSePuede_DevuelveElMotivo_YSinInformarNuncaRechaza()
        {
            NVEntities db = DbConCondiciones(new CondPagoCliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0", PlazosPago = "CONTADO" });
            var controller = new PedidosVentaController(db);
            var informado = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0", plazosPago = "30/60", periodoFacturacion = "NRM", modoFacturacion = M.POR_ENTREGAS };
            var sinInformar = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0", plazosPago = "30/60", periodoFacturacion = "NRM", mantenerJunto = false };

            Assert.AreEqual(ModosFacturacionPermitidos.MOTIVO_PLAZOS, controller.NormalizarModoFacturacion(informado, null));
            Assert.IsNull(controller.NormalizarModoFacturacion(sinInformar, null), "Nesto y NestoApp no mandan el modo: se deriva del bit y el trigger sigue haciendo lo suyo");
            Assert.AreEqual(M.POR_ENTREGAS, sinInformar.modoFacturacion);
        }

        [TestMethod]
        public void PostModoFacturacionSugerido_PlazosDeLaFichaDelContactoDeCobro_TodosPermitidos()
        {
            NVEntities db = DbConCondiciones(new CondPagoCliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "1", PlazosPago = "30/60" });
            var controller = new PedidosVentaController(db);
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 0, cliente = "15191", contacto = "0", contactoCobro = "1", plazosPago = "30/60", periodoFacturacion = "NRM" };

            var resultado = controller.PostModoFacturacionSugerido(pedido) as OkNegotiatedContentResult<ModosFacturacionPermitidos.Sugerencia>;

            Assert.IsNotNull(resultado);
            CollectionAssert.AreEqual(new List<byte> { 1, 2, 3 }, resultado.Content.ModosPermitidos);
            Assert.AreEqual(M.POR_ENTREGAS, resultado.Content.Modo);
        }
    }
}
