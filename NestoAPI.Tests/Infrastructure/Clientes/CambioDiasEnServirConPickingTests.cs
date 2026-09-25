using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#541 (Carlos, 25/09/26, opción B): cerrar un día en la ficha con pedidos ya en picking se rechaza
    /// hasta que el usuario acepte avisar a almacén; entonces se guarda y se manda el correo, solo a almacén.
    /// Caso real: pedido 925633, cliente 27120/3, «cierra los lunes» puesto con el picking 99531 ya sacado.
    /// </summary>
    [TestClass]
    public class CambioDiasEnServirConPickingTests
    {
        private static LinPedidoVta Linea(int pedido, short estado, int? picking) =>
            new LinPedidoVta { Empresa = "1", Número = pedido, Nº_Cliente = "27120", Contacto = "3", Estado = estado, Picking = picking };

        private static Cliente Cliente27120(string dias = "11111") =>
            new Cliente { Empresa = "1", Nº_Cliente = "27120", Contacto = "3", Nombre = "LAURA VADILLO SECO", DiasEnServir = dias };

        [TestMethod]
        public void DiasQueSeCierran_SoloLosQueEstabanAbiertosYPasanACerrados()
        {
            CollectionAssert.AreEqual(new[] { "lunes" }, CambioDiasEnServirConPicking.DiasQueSeCierran("11111", "01111"));
            CollectionAssert.AreEqual(new[] { "lunes", "viernes" }, CambioDiasEnServirConPicking.DiasQueSeCierran("11111", "01110"));
            Assert.AreEqual(0, CambioDiasEnServirConPicking.DiasQueSeCierran("01111", "11111").Count, "Abrir días nunca molesta");
            Assert.AreEqual(0, CambioDiasEnServirConPicking.DiasQueSeCierran("01111", "01111").Count);
            Assert.AreEqual(0, CambioDiasEnServirConPicking.DiasQueSeCierran("11111", null).Count);
            // Sin valor anterior (ficha vieja) se considera que abría
            CollectionAssert.AreEqual(new[] { "lunes" }, CambioDiasEnServirConPicking.DiasQueSeCierran(null, "01111"));
            // Los char de la BD llegan con relleno
            CollectionAssert.AreEqual(new[] { "lunes" }, CambioDiasEnServirConPicking.DiasQueSeCierran("11111 ", "01111 "));
        }

        [TestMethod]
        public void PedidosConPickingVivo_SoloLineasVivasConPicking_SinRepetir()
        {
            var lineas = new List<LinPedidoVta>
            {
                Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 99531),
                Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 99531),
                Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 0),      // sin picking
                Linea(925700, Constantes.EstadosLineaVenta.PENDIENTE, 99540),
                Linea(925000, Constantes.EstadosLineaVenta.ALBARAN, 99000),   // ya salió: no cuenta
                Linea(925001, Constantes.EstadosLineaVenta.FACTURA, 98000),
                Linea(925002, Constantes.EstadosLineaVenta.EN_CURSO, null)
            };

            CollectionAssert.AreEqual(new[] { 925633, 925700 }, CambioDiasEnServirConPicking.PedidosConPickingVivo(lineas));
        }

        [TestMethod]
        public void Comprobar_SinCerrarNingunDia_NoMiraLasLineasNiAvisa()
        {
            bool consultadas = false;

            MailMessage aviso = CambioDiasEnServirConPicking.Comprobar(Cliente27120("01111"), "11111",
                () => { consultadas = true; return new List<LinPedidoVta>(); }, confirmado: false, usuario: "u");

            Assert.IsNull(aviso);
            Assert.IsFalse(consultadas, "Las líneas solo se consultan cuando el cambio cierra un día");
        }

        [TestMethod]
        public void Comprobar_CierraUnDiaSinPedidosConPicking_NoAvisa()
        {
            MailMessage aviso = CambioDiasEnServirConPicking.Comprobar(Cliente27120(), "01111",
                () => new List<LinPedidoVta> { Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 0) }, confirmado: false, usuario: "u");

            Assert.IsNull(aviso);
        }

        [TestMethod]
        public void Comprobar_CierraUnDiaConPicking_SinConfirmar_RechazaConCodigoYPedidos()
        {
            DiasEnServirConPickingException ex = Assert.ThrowsException<DiasEnServirConPickingException>(() =>
                CambioDiasEnServirConPicking.Comprobar(Cliente27120(), "01111",
                    () => new List<LinPedidoVta> { Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 99531) }, confirmado: false, usuario: "u"));

            Assert.AreEqual(CambioDiasEnServirConPicking.CODIGO, ex.Context.ErrorCode);
            Assert.AreEqual("27120", ex.Context.Cliente);
            Assert.AreEqual("3", ex.Context.AdditionalData["contacto"]);
            CollectionAssert.AreEqual(new[] { 925633 }, (List<int>)ex.Context.AdditionalData["pedidos"]);
            StringAssert.Contains(ex.Message, "925633");
            StringAssert.Contains(ex.Message, "lunes");
            StringAssert.Contains(ex.Message, "¿Avisamos a almacén?");
        }

        [TestMethod]
        public void Comprobar_Confirmado_DevuelveElCorreoSoloAAlmacen()
        {
            MailMessage aviso = CambioDiasEnServirConPicking.Comprobar(Cliente27120(), "01111",
                () => new List<LinPedidoVta> { Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 99531) }, confirmado: true, usuario: "NUEVAVISION\\MPP");

            Assert.IsNotNull(aviso);
            Assert.AreEqual(1, aviso.To.Count);
            Assert.AreEqual(Constantes.Correos.ALMACEN, aviso.To[0].Address);
            Assert.AreEqual(0, aviso.CC.Count, "Solo a almacén (Carlos)");
            StringAssert.Contains(aviso.Subject, "27120/3");
            StringAssert.Contains(aviso.Subject, "lunes");
            StringAssert.Contains(aviso.Subject, "925633");
            StringAssert.Contains(aviso.Body, "NUEVAVISION\\MPP");
            StringAssert.Contains(aviso.Body, "LAURA VADILLO SECO");
            StringAssert.Contains(aviso.Body, "11111 → 01111");
        }

        // El gestor: PrepararClienteModificar rechaza o prepara el aviso, y ModificarCliente lo manda tras guardar

        private static (GestorClientes gestor, NVEntities db, Cliente clienteDB, IServicioCorreoElectronico correo) Preparar(params LinPedidoVta[] lineas)
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            Cliente clienteDB = Cliente27120();
            clienteDB.Vendedore = new Vendedor { Estado = 0 };
            clienteDB.CCCs = new List<CCC>();
            clienteDB.PersonasContactoClientes = new List<PersonaContactoCliente>();
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>._, A<string>._, A<string>._, A<string>._)).Returns(clienteDB);
            NVEntities db = A.Fake<NVEntities>();
            IQueryable<LinPedidoVta> datos = lineas.AsQueryable();
            DbSet<LinPedidoVta> fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>());
            A.CallTo(() => ((IQueryable<LinPedidoVta>)fakeLineas).Provider).Returns(datos.Provider);
            A.CallTo(() => ((IQueryable<LinPedidoVta>)fakeLineas).Expression).Returns(datos.Expression);
            A.CallTo(() => ((IQueryable<LinPedidoVta>)fakeLineas).ElementType).Returns(datos.ElementType);
            A.CallTo(() => ((IQueryable<LinPedidoVta>)fakeLineas).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            var gestor = new GestorClientes(servicio, A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(A.Fake<ISincronizacionEventPublisher>()))
            {
                ServicioCorreo = correo
            };
            return (gestor, db, clienteDB, correo);
        }

        private static ClienteCrear Cambio(string dias, bool confirmar = false) => new ClienteCrear
        {
            Empresa = "1", Cliente = "27120", Contacto = "3", Nombre = "LAURA VADILLO SECO", Nif = "12345678Z",
            DiasEnServir = dias, ConfirmarDiasEnServirConPicking = confirmar, Usuario = "NUEVAVISION\\MPP",
            PersonasContacto = new List<PersonaContactoDTO>()
        };

        [TestMethod]
        public async Task PrepararClienteModificar_CierraElLunesConPicking_SinConfirmar_RechazaYNoTocaLaFicha()
        {
            var (gestor, db, clienteDB, _) = Preparar(Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 99531));

            await Assert.ThrowsExceptionAsync<DiasEnServirConPickingException>(() => gestor.PrepararClienteModificar(Cambio("01111"), db));

            Assert.AreEqual("11111", clienteDB.DiasEnServir);
        }

        [TestMethod]
        public async Task PrepararClienteModificar_CierraElLunesConPicking_Confirmado_CambiaLaFichaYDejaElAvisoQueSeMandaTrasGuardar()
        {
            var (gestor, db, clienteDB, correo) = Preparar(Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 99531));

            Cliente resultado = await gestor.PrepararClienteModificar(Cambio("01111", confirmar: true), db);

            Assert.AreEqual("01111", resultado.DiasEnServir);
            Assert.IsNotNull(gestor.avisoAlmacenPendiente, "El correo va DESPUÉS de guardar, no antes");
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();

            gestor.EnviarAvisoAlmacenPendiente(clienteDB);

            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m => m.To[0].Address == Constantes.Correos.ALMACEN && m.Subject.Contains("925633"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PrepararClienteModificar_AbreUnDia_NoPreguntaNiAvisa()
        {
            var (gestor, db, clienteDB, correo) = Preparar(Linea(925633, Constantes.EstadosLineaVenta.EN_CURSO, 99531));
            clienteDB.DiasEnServir = "01111";

            Cliente resultado = await gestor.PrepararClienteModificar(Cambio("11111"), db);

            Assert.AreEqual("11111", resultado.DiasEnServir);
            Assert.IsNull(gestor.avisoAlmacenPendiente);
        }
    }
}
