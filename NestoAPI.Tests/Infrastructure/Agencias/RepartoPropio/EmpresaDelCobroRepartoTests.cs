using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#801: los cobros del reparto propio van a la empresa del PEDIDO, no a EnviosAgencia.Empresa (siempre «1»). Caso real del
    /// 03/11/26: 5825 / pedido 929679 (empresa 3), 137,31 € en efectivo, contabilizado en la 1 a cuenta en vez de liquidar la GB2601735.
    /// </summary>
    [TestClass]
    public class EmpresaDelCobroRepartoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 3);
        private const string USUARIO = @"NUEVAVISION\Miguel";
        private const string CAJA_MIGUEL = "57000032";

        // ================================================================ la regla (pura)

        [TestMethod]
        public void DeEnvio_ConPedidoDeLaEmpresa3_LaDelPedidoAunqueElEnvioDigaLa1()
        {
            Assert.AreEqual("3", EmpresaDelCobroReparto.DeEnvio("3  ", "1  "));
        }

        [TestMethod]
        public void DeEnvio_SinPedido_LaDelEnvio_YSinNadaLa1()
        {
            Assert.AreEqual("1", EmpresaDelCobroReparto.DeEnvio(null, "1  "));
            Assert.AreEqual("3", EmpresaDelCobroReparto.DeEnvio("  ", "3  "));
            Assert.AreEqual(Constantes.Empresas.EMPRESA_POR_DEFECTO, EmpresaDelCobroReparto.DeEnvio(null, null));
        }

        [TestMethod]
        public void DeACuenta_EfectosMarcadosDeUnaSolaEmpresa_EsaEmpresa()
        {
            Assert.AreEqual("3", EmpresaDelCobroReparto.DeACuenta(new[] { "3  ", "3" }, "1"));
        }

        [TestMethod]
        public void DeACuenta_EfectosMarcadosDeDosEmpresas_La1()
        {
            Assert.AreEqual("1", EmpresaDelCobroReparto.DeACuenta(new[] { "1  ", "3  " }, "3"));
        }

        [TestMethod]
        public void DeACuenta_SinMarcados_LaDeLaUltimaPendiente_YSinNadaLa1()
        {
            Assert.AreEqual("3", EmpresaDelCobroReparto.DeACuenta(new string[0], "3  "));
            Assert.AreEqual("1", EmpresaDelCobroReparto.DeACuenta(null, null));
            Assert.AreEqual("1", EmpresaDelCobroReparto.DeACuenta(new string[0], ""));
        }

        [TestMethod]
        public async Task EmpresaDelPedidoAsync_LeeCabPedidoVtaPorNumero()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<CabPedidoVta> pedidos = FakeDbSet(new List<CabPedidoVta>
            {
                new CabPedidoVta { Empresa = "1  ", Número = 929753 },
                new CabPedidoVta { Empresa = "3  ", Número = 929679 }
            });
            A.CallTo(() => db.CabPedidoVtas).Returns(pedidos);

            Assert.AreEqual("3", await EmpresaDelCobroReparto.DeEnvioAsync(db, new EnviosAgencia { Empresa = "1  ", Pedido = 929679 }));
            Assert.AreEqual("1", await EmpresaDelCobroReparto.DeEnvioAsync(db, new EnviosAgencia { Empresa = "1  ", Pedido = 999999 }),
                "Sin pedido en CabPedidoVta: la del envío");
            Assert.AreEqual("1", await EmpresaDelCobroReparto.DeEnvioAsync(db, new EnviosAgencia { Empresa = "1  ", Pedido = null }));
        }

        // ================================================================ el efectivo al entregar (TramitacionEnviosService)

        private static EnviosAgencia Envio5825() => new EnviosAgencia
        {
            Numero = 250446,
            Empresa = "1  ",
            Cliente = "5825      ",
            Contacto = "0  ",
            Pedido = 929679,
            Vendedor = "NV ",
            Reembolso = 137.31M,
            Empresa1 = new Empresa { Número = "1  ", FormaPagoEfectivo = "EFC", DelegaciónVarios = "ALG", FormaVentaVarios = "DIR" },
            AgenciasTransporte = new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Nombre = "Reparto propio" }
        };

        private static NVEntities DbConFacturaDeLa3(out IContabilidadService contabilidad, out List<PreContabilidad> lineas)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<ExtractoCliente> extractos = FakeDbSet(new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Nº_Orden = 7001, Empresa = "3", Número = "5825      ", Contacto = "0  ", Nº_Documento = "GB2601735", Efecto = "1",
                    ImportePdte = 137.31M, Importe = 137.31M, Fecha = HOY, Estado = Constantes.ExtractosCliente.Estados.NORMAL,
                    Delegación = "ALG", FormaVenta = "TEL", Ruta = "AT"
                }
            });
            DbSet<Empresa> empresas = FakeDbSet(new List<Empresa>
            {
                new Empresa { Número = "1", FormaPagoEfectivo = "EFC", DelegaciónVarios = "ALG", FormaVentaVarios = "DIR" },
                new Empresa { Número = "3", FormaPagoEfectivo = "EF3", DelegaciónVarios = "GB", FormaVentaVarios = "VAR" }
            });
            A.CallTo(() => db.ExtractosCliente).Returns(extractos);
            A.CallTo(() => db.Empresas).Returns(empresas);
            IContabilidadService fake = A.Fake<IContabilidadService>();
            contabilidad = fake;
            var registradas = new List<PreContabilidad>();
            lineas = registradas;
            A.CallTo(() => fake.CrearLineas(db, A<List<PreContabilidad>>._))
                .ReturnsLazily((NVEntities d, List<PreContabilidad> l) => { registradas.AddRange(l); return Task.FromResult(l.Count); });
            A.CallTo(() => fake.ContabilizarDiario(db, "3", "_Reembolso", USUARIO)).Returns(164925);
            return db;
        }

        [TestMethod]
        public async Task ContabilizarReembolso_PedidoDeLaEmpresa3_ApunteEnLa3LiquidandoSuFactura()
        {
            NVEntities db = DbConFacturaDeLa3(out IContabilidadService contabilidad, out List<PreContabilidad> lineas);
            var servicio = new TramitacionEnviosService(db, contabilidad, () => HOY);

            int asiento = await servicio.ContabilizarReembolsoAsync(Envio5825(), USUARIO, 137.31M, CAJA_MIGUEL, "3  ");

            Assert.AreEqual(164925, asiento, "Se contabiliza el diario de la empresa 3");
            PreContabilidad linea = lineas.Single();
            Assert.AreEqual("3", linea.Empresa);
            Assert.AreEqual(7001, linea.Liquidado, "Liquida la factura de la 3, no queda a cuenta");
            Assert.AreEqual("GB2601735", linea.Nº_Documento);
            Assert.AreEqual(137.31M, linea.Haber);
            Assert.AreEqual(CAJA_MIGUEL, linea.Contrapartida);
            Assert.AreEqual("EF3", linea.FormaPago, "La forma de pago en efectivo de la empresa 3");
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ContabilizarReembolso_SinEmpresaExplicita_ComoSiempreEnLaDelEnvio()
        {
            // Las agencias de fuera (y la sobrecarga de 4 parámetros) siguen con envio.Empresa: aquí la 1, donde no hay nada pendiente
            NVEntities db = DbConFacturaDeLa3(out IContabilidadService contabilidad, out List<PreContabilidad> lineas);
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", USUARIO)).Returns(1207304);
            var servicio = new TramitacionEnviosService(db, contabilidad, () => HOY);

            int asiento = await servicio.ContabilizarReembolsoAsync(Envio5825(), USUARIO, 137.31M, CAJA_MIGUEL);

            Assert.AreEqual(1207304, asiento);
            PreContabilidad linea = lineas.Single();
            Assert.AreEqual("1", linea.Empresa);
            Assert.IsNull(linea.Liquidado);
            Assert.AreEqual("EFC", linea.FormaPago);
        }

        [TestMethod]
        public async Task BuscarMovimientoALiquidar_EnLaEmpresaDelPedido_EncuentraLaFacturaDeLa3()
        {
            NVEntities db = DbConFacturaDeLa3(out IContabilidadService contabilidad, out _);
            var servicio = new TramitacionEnviosService(db, contabilidad, () => HOY);

            ExtractoCliente movimiento = await servicio.BuscarMovimientoALiquidarAsync(Envio5825(), 137.31M, "3");

            Assert.IsNotNull(movimiento);
            Assert.AreEqual("GB2601735", movimiento.Nº_Documento);
        }

        // ================================================================ utilidades

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            DbSet<T> set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            IQueryable<T> data = datos.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)set).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)set).Provider).Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)set).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            return set;
        }
    }
}
