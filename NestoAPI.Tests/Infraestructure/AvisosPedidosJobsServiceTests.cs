using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Notificaciones;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// TNV#66: la pasada del job que avisa por push de los cambios de estado. Lo que se prueba
    /// aquí es la orquestación: a quién se mira, qué se registra y cuántos avisos salen.
    /// </summary>
    [TestClass]
    public class AvisosPedidosJobsServiceTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 9, 4, 17, 0, 0);
        private const string CLIENTE = "15816";

        private NVEntities db;
        private DbSet<DispositivoNotificacion> fakeDispositivos;
        private IServicioPedidosCliente servicioPedidos;
        private IAlmacenEstadoNotificadoPedido almacen;
        private IServicioNotificacionesPush push;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeDispositivos = A.Fake<DbSet<DispositivoNotificacion>>(o =>
                o.Implements<IQueryable<DispositivoNotificacion>>()
                 .Implements<IDbAsyncEnumerable<DispositivoNotificacion>>());
            A.CallTo(() => db.DispositivosNotificaciones).Returns(fakeDispositivos);

            servicioPedidos = A.Fake<IServicioPedidosCliente>();
            almacen = A.Fake<IAlmacenEstadoNotificadoPedido>();
            push = A.Fake<IServicioNotificacionesPush>();

            ConDispositivosDe(CLIENTE);
            A.CallTo(() => almacen.Obtener(A<string>._, A<IReadOnlyCollection<int>>._))
                .Returns(new Dictionary<int, EstadoNotificadoPedido>());
        }

        [TestMethod]
        public async Task Pasada_SinNadieConLaApp_NoMiraNingunPedido()
        {
            // Recorrer todos los pedidos de la empresa cada media hora para no avisar a nadie
            // sería tirar consultas.
            ConDispositivosDe();

            int avisados = await AvisosPedidosJobsService.AvisarCambiosDeEstado(
                db, servicioPedidos, almacen, push, AHORA);

            Assert.AreEqual(0, avisados);
            A.CallTo(() => servicioPedidos.LeerPedidosRecientes(A<string>._, A<string>._, A<int>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Pasada_PrimeraVezQueSeVeElPedido_LoRegistraPeroNoAvisa()
        {
            ConPedidos(Pedido(EstadoPedidoCliente.Enviado));

            int avisados = await AvisosPedidosJobsService.AvisarCambiosDeEstado(
                db, servicioPedidos, almacen, push, AHORA);

            Assert.AreEqual(0, avisados);
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .MustNotHaveHappened();
            A.CallTo(() => almacen.RegistrarEstado("1", 925368, "Enviado", AHORA))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Pasada_ElPedidoAcabaDeSalir_AvisaYLoApunta()
        {
            ConPedidos(Pedido(EstadoPedidoCliente.Enviado));
            ConRegistro(925368, EstadoPedidoCliente.EnPreparacion);

            int avisados = await AvisosPedidosJobsService.AvisarCambiosDeEstado(
                db, servicioPedidos, almacen, push, AHORA);

            Assert.AreEqual(1, avisados);
            A.CallTo(() => push.EnviarACliente("1", CLIENTE, A<NotificacionPushDTO>.That.Matches(
                n => n.Datos["pedido"] == "925368" && n.Datos["estado"] == "Enviado")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => almacen.RegistrarAviso("1", 925368, "Enviado", AHORA))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Pasada_ElEstadoSeRegistraAunqueNoSeAvise()
        {
            // Es lo que da la fecha "desde cuándo está así", de la que depende el recordatorio
            // de pago: sin registrarlo, un pedido sin pagar no cumpliría nunca la espera.
            ConPedidos(Pedido(EstadoPedidoCliente.EnPreparacion));
            ConRegistro(925368, EstadoPedidoCliente.Recibido);

            int avisados = await AvisosPedidosJobsService.AvisarCambiosDeEstado(
                db, servicioPedidos, almacen, push, AHORA);

            Assert.AreEqual(0, avisados);
            A.CallTo(() => almacen.RegistrarEstado("1", 925368, "EnPreparacion", AHORA))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Pasada_UnClienteQueFalla_NoDejaSinAvisarALosDemas()
        {
            ConDispositivosDe("15816", "32624");
            A.CallTo(() => servicioPedidos.LeerPedidosRecientes("1", "15816", A<int>._))
                .Throws(new Exception("la base de datos ha dicho que no"));
            A.CallTo(() => servicioPedidos.LeerPedidosRecientes("1", "32624", A<int>._))
                .Returns(Task.FromResult(new List<PedidoClienteResumenDTO> { Pedido(EstadoPedidoCliente.Enviado) }));
            ConRegistro(925368, EstadoPedidoCliente.EnPreparacion);

            int avisados = await AvisosPedidosJobsService.AvisarCambiosDeEstado(
                db, servicioPedidos, almacen, push, AHORA);

            Assert.AreEqual(1, avisados);
        }

        private void ConDispositivosDe(params string[] clientes)
        {
            List<DispositivoNotificacion> dispositivos = clientes
                .Select((c, i) => new DispositivoNotificacion
                {
                    Id = i + 1,
                    Cliente = c,
                    Empresa = "1  ",
                    Aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS,
                    Activo = true
                })
                .ToList();

            // Un dispositivo de otra app no puede colarse: esos avisos son de la app de tiendas.
            dispositivos.Add(new DispositivoNotificacion
            {
                Id = 99,
                Cliente = "99999",
                Empresa = "1  ",
                Aplicacion = Constantes.Aplicaciones.NESTO_APP,
                Activo = true
            });

            ConfigurarFakeDbSet(fakeDispositivos, dispositivos.AsQueryable());
        }

        private void ConPedidos(params PedidoClienteResumenDTO[] pedidos)
        {
            A.CallTo(() => servicioPedidos.LeerPedidosRecientes(A<string>._, A<string>._, A<int>._))
                .Returns(Task.FromResult(pedidos.ToList()));
        }

        private void ConRegistro(int pedido, EstadoPedidoCliente estado)
        {
            A.CallTo(() => almacen.Obtener(A<string>._, A<IReadOnlyCollection<int>>._))
                .Returns(new Dictionary<int, EstadoNotificadoPedido>
                {
                    {
                        pedido,
                        new EstadoNotificadoPedido
                        {
                            Empresa = "1",
                            Pedido = pedido,
                            Estado = estado.ToString(),
                            FechaEstado = AHORA.AddHours(-2)
                        }
                    }
                });
        }

        private static PedidoClienteResumenDTO Pedido(EstadoPedidoCliente estado)
        {
            return new PedidoClienteResumenDTO
            {
                Numero = 925368,
                Fecha = AHORA.Date,
                Total = 24.20M,
                Estado = estado,
                EstadoTexto = "da igual"
            };
        }

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
