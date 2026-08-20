using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#332: selector de efectos cobrables (núcleo común + estrategia SEPA). Es el
    /// modo simulación de la remesa y donde vive el gating de entrega (#172) y la puerta de
    /// neteo — reglas que la remesa de tarjetas (#181) consumirá de aquí, no reimplementará.
    /// </summary>
    [TestClass]
    public class SelectorEfectosCobrablesTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 7, 21);

        private NVEntities db;
        private DbSet<ExtractoCliente> fakeExtractos;
        private DbSet<LinPedidoVta> fakeLineas;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<CCC> fakeCccs;
        private List<string> estadosQueBloquean;
        private List<int> agenciasConSeguimiento;
        private SelectorEfectosCobrables selector;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeExtractos = A.Fake<DbSet<ExtractoCliente>>(o => o.Implements<IQueryable<ExtractoCliente>>().Implements<IDbAsyncEnumerable<ExtractoCliente>>());
            fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeCccs = A.Fake<DbSet<CCC>>(o => o.Implements<IQueryable<CCC>>().Implements<IDbAsyncEnumerable<CCC>>());
            A.CallTo(() => db.ExtractosCliente).Returns(fakeExtractos);
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.CCCs).Returns(fakeCccs);
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>().AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>().AsQueryable());
            // NestoAPI#381: fichas bancarias válidas por defecto para los clientes de los tests
            ConfigurarFakeDbSet(fakeCccs, new List<CCC> { Ficha("15191"), Ficha("30676") }.AsQueryable());
            estadosQueBloquean = new List<string>();
            // Fallo 20/08/26: el gating solo mira envíos de agencias CON seguimiento. Los envíos
            // de los tests no fijan Agencia (0), así que 0 cuenta como "con seguimiento" aquí.
            agenciasConSeguimiento = new List<int> { 0 };
            selector = new SelectorEfectosCobrables(db, e => Task.FromResult(estadosQueBloquean),
                () => agenciasConSeguimiento.ToArray());
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        // OJO gotcha de fakes (#313): el char() de BD lleva padding y el fake compara ordinal,
        // así que aquí los datos van SIN espacios de relleno.
        private static ExtractoCliente Efecto(int id = 1, string cliente = "15191", decimal pendiente = 250.50m,
            string ccc = "1", DateTime? fecha = null, DateTime? vencimiento = null, string documento = "NV2612000",
            string tipoApunte = "2", string estado = null)
        {
            return new ExtractoCliente
            {
                Empresa = "1",
                Nº_Orden = id,
                Número = cliente,
                Contacto = "0",
                TipoApunte = tipoApunte,
                ImportePdte = pendiente,
                CCC = ccc,
                Fecha = fecha ?? HOY.AddDays(-5),
                FechaVto = vencimiento ?? HOY.AddDays(-1),
                Nº_Documento = documento,
                Estado = estado
            };
        }

        // NestoAPI#381: ficha bancaria (CCC) del cliente. Por defecto con el IBAN de ejemplo
        // válido ES91 2100 0418 45 0200051332 (mod-97 correcto).
        private static CCC Ficha(string cliente = "15191", string numero = "1", string pais = "ES",
            string dcIban = "91", string entidad = "2100", string oficina = "0418", string dc = "45",
            string cuenta = "0200051332")
        {
            return new CCC
            {
                Empresa = "1",
                Cliente = cliente,
                Contacto = "0",
                Número = numero,
                Pais = pais,
                DC_IBAN = dcIban,
                Entidad = entidad,
                Oficina = oficina,
                DC = dc,
                Nº_Cuenta = cuenta
            };
        }

        [TestMethod]
        public async Task CandidatosSepa_CarteraVencidaConCcc_PreseleccionadaSinMotivo()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto() }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.AreEqual(1, candidatos.Count);
            Assert.IsTrue(candidatos.Single().Preseleccionado);
            Assert.IsNull(candidatos.Single().Motivo);
            Assert.IsFalse(candidatos.Single().ClienteConNegativos);
        }

        [TestMethod]
        public async Task CandidatosSepa_ConHasta_IncluyeVencimientosFuturosHastaEsaFecha()
        {
            // NestoAPI#345: un viernes se giran también los efectos del fin de semana (o los de
            // mañana si es festivo): hasta = límite de VENCIMIENTO incluido.
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, vencimiento: HOY.AddDays(-1)),
                Efecto(id: 2, vencimiento: HOY.AddDays(2)),
                Efecto(id: 3, vencimiento: HOY.AddDays(3))
            }.AsQueryable());

            List<EfectoCandidatoDTO> sinHasta = await selector.CandidatosSepa("1", HOY);
            List<EfectoCandidatoDTO> conHasta = await selector.CandidatosSepa("1", HOY, hasta: HOY.AddDays(2));

            Assert.AreEqual(1, sinHasta.Count, "Sin hasta: solo los vencidos a hoy (clásico)");
            Assert.AreEqual(2, conHasta.Count, "Con hasta: entran también los que vencen dentro del límite");
            Assert.IsTrue(conHasta.Any(c => c.Id == 2));
            Assert.IsFalse(conHasta.Any(c => c.Id == 3), "Más allá del límite sigue fuera");
        }

        [TestMethod]
        public async Task CandidatosSepa_ConHasta_LosNacidosHoySiguenExcluidos()
        {
            // El margen de "lo facturado hoy queda fuera" se ancla a HOY, no a la fecha hasta
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, fecha: HOY, vencimiento: HOY)
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY, hasta: HOY.AddDays(3));

            Assert.AreEqual(0, candidatos.Count, "Un efecto con Fecha de hoy no entra aunque el hasta sea futuro");
        }

        [TestMethod]
        public async Task CandidatosSepa_ElNucleoExcluyeLoQueNoEsCarteraVencidaPendiente()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, tipoApunte: "1"),                              // factura, no cartera
                Efecto(id: 2, pendiente: 0m),                                // nada pendiente
                Efecto(id: 3, vencimiento: HOY.AddDays(10)),                 // aún no vencido
                Efecto(id: 4, fecha: HOY),                                   // facturado hoy (margen)
                Efecto(id: 5)                                                // válido
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.AreEqual(1, candidatos.Count);
            Assert.AreEqual(5, candidatos.Single().Id);
        }

        [TestMethod]
        public async Task CandidatosSepa_EstrategiaSepa_SinCccQuedaFuera()
        {
            // La estrategia de tarjeta (#181) hará lo contrario: TAR + CCC vacío + token
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, ccc: null),
                Efecto(id: 2, ccc: ""),
                Efecto(id: 3, ccc: "1")
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.AreEqual(1, candidatos.Count);
            Assert.AreEqual(3, candidatos.Single().Id);
        }

        [TestMethod]
        public async Task CandidatosSepa_EnvioSinEntregar_RetenidoConMotivo()
        {
            // Gating #172: factura → línea → pedido → envío con Estado != ENTREGADO
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "NV2612001"),
                Efecto(id: 2, documento: "NV2612002")
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 922001, Nº_Factura = "NV2612001" },
                new LinPedidoVta { Empresa = "1", Número = 922002, Nº_Factura = "NV2612002" }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>
            {
                new EnviosAgencia { Numero = 1, Pedido = 922001, Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO, Fecha = HOY.AddDays(-3) },
                new EnviosAgencia { Numero = 2, Pedido = 922002, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Fecha = HOY.AddDays(-3) }
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            EfectoCandidatoDTO retenido = candidatos.Single(c => c.Id == 1);
            EfectoCandidatoDTO liberado = candidatos.Single(c => c.Id == 2);
            Assert.IsFalse(retenido.Preseleccionado);
            StringAssert.Contains(retenido.Motivo, "sin confirmar la entrega");
            Assert.IsTrue(liberado.Preseleccionado, "Con el envío ENTREGADO el efecto se libera");
        }

        [TestMethod]
        public async Task CandidatosSepa_EnvioAnteriorAlCorteDelPoll_SeLibera()
        {
            // Matiz de Carlos 21/07: la señal correcta es la FECHA DE CORTE del poll de
            // seguimiento, no un timeout de N días. Un envío anterior al corte (caso real
            // NV2515520 de sept/2025, 'tramitado' eterno) no tiene seguimiento posible → no
            // retiene. Uno POSTERIOR al corte sin entregar retiene SIN timeout (el poll lo
            // sigue; puede estar perdido o en reparto).
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "NV2515520"),
                Efecto(id: 2, documento: "NV2612002")
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 900001, Nº_Factura = "NV2515520" },
                new LinPedidoVta { Empresa = "1", Número = 922002, Nº_Factura = "NV2612002" }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>
            {
                new EnviosAgencia { Numero = 1, Pedido = 900001, Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO,
                    Fecha = SeguimientoEnviosJobsService.FECHA_CORTE.AddDays(-30) },  // pre-corte
                new EnviosAgencia { Numero = 2, Pedido = 922002, Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO,
                    Fecha = SeguimientoEnviosJobsService.FECHA_CORTE.AddDays(5) }     // post-corte, 45+ días sin entregar
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.IsTrue(candidatos.Single(c => c.Id == 1).Preseleccionado, "Pre-corte: sin seguimiento posible, no retiene");
            Assert.IsFalse(candidatos.Single(c => c.Id == 2).Preseleccionado, "Post-corte sin entregar: retiene sin timeout");
        }

        [TestMethod]
        public async Task CandidatosSepa_EnvioIncidentadoODevuelto_RetienenConSuMotivo()
        {
            // Carlos 21/07: incidentado no se mete mientras dure; devuelto = el cobro no
            // procede por remesa (abono o gestión manual).
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "NV2612001"),
                Efecto(id: 2, documento: "NV2612002")
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 922001, Nº_Factura = "NV2612001" },
                new LinPedidoVta { Empresa = "1", Número = 922002, Nº_Factura = "NV2612002" }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>
            {
                new EnviosAgencia { Numero = 1, Pedido = 922001, Estado = Constantes.Agencias.ESTADO_INCIDENTADO, Fecha = HOY.AddDays(-3) },
                new EnviosAgencia { Numero = 2, Pedido = 922002, Estado = Constantes.Agencias.ESTADO_DEVUELTO, Fecha = HOY.AddDays(-3) }
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            StringAssert.Contains(candidatos.Single(c => c.Id == 1).Motivo, "INCIDENTADO");
            StringAssert.Contains(candidatos.Single(c => c.Id == 2).Motivo, "DEVUELTO");
            Assert.IsTrue(candidatos.All(c => !c.Preseleccionado));
            // Fallo 20/08/26: incidentado se puede forzar en la remesa; devuelto NUNCA
            Assert.IsTrue(candidatos.Single(c => c.Id == 1).Forzable, "Incidentado: forzable");
            Assert.IsFalse(candidatos.Single(c => c.Id == 2).Forzable, "Devuelto: no forzable");
        }

        [TestMethod]
        public async Task CandidatosSepa_EnvioDeAgenciaSinSeguimiento_NoRetiene()
        {
            // Fallo 20/08/26 (caso real 3028653): un envío de Correos Express (u otra agencia
            // sin integración de seguimiento) se queda en 'tramitado' PARA SIEMPRE porque el
            // poll no la actualiza — retenía el efecto eternamente y nunca iba al banco. El
            // gating solo puede mirar agencias cuyo estado SÍ actualizamos hasta Entregado.
            agenciasConSeguimiento = new List<int> { 6 };
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "NV2612001"),
                Efecto(id: 2, documento: "NV2612002")
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 922001, Nº_Factura = "NV2612001" },
                new LinPedidoVta { Empresa = "1", Número = 922002, Nº_Factura = "NV2612002" }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>
            {
                // Correos Express (sin seguimiento): tramitado eterno, NO debe retener
                new EnviosAgencia { Numero = 1, Pedido = 922001, Agencia = 9, Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO, Fecha = HOY.AddDays(-3) },
                // Agencia con seguimiento: sin entregar, SÍ retiene
                new EnviosAgencia { Numero = 2, Pedido = 922002, Agencia = 6, Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO, Fecha = HOY.AddDays(-3) }
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.IsTrue(candidatos.Single(c => c.Id == 1).Preseleccionado,
                "Agencia sin seguimiento: su 'tramitado' no significa nada, no retiene");
            EfectoCandidatoDTO retenido = candidatos.Single(c => c.Id == 2);
            Assert.IsFalse(retenido.Preseleccionado, "Agencia con seguimiento sin entregar: retiene");
            Assert.IsTrue(retenido.Forzable, "La retención por entrega pendiente se puede forzar");
        }

        [TestMethod]
        public async Task CandidatosSepa_EstadoDelEfectoQueBloquea_RetenidoConMotivo()
        {
            // Matiz de Carlos 21/07: Estado NULL entra; con estado, solo si EstadosExtracto
            // no lo bloquea (BloquearLiquidación=0); bloqueado → no se puede remesar.
            estadosQueBloquean.Add("7");
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, estado: null),
                Efecto(id: 2, estado: "5"),
                Efecto(id: 3, estado: "7")
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.IsTrue(candidatos.Single(c => c.Id == 1).Preseleccionado, "Estado NULL entra");
            Assert.IsTrue(candidatos.Single(c => c.Id == 2).Preseleccionado, "Estado sin bloqueo entra");
            EfectoCandidatoDTO bloqueado = candidatos.Single(c => c.Id == 3);
            Assert.IsFalse(bloqueado.Preseleccionado);
            StringAssert.Contains(bloqueado.Motivo, "bloquea la liquidación");
        }

        [TestMethod]
        public async Task CandidatosSepa_FacturaSinEnvios_SeLibera()
        {
            // Mostrador/servicios: sin envíos de agencia se preserva la política actual
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto() }.AsQueryable());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 922003, Nº_Factura = "NV2612000" }
            }.AsQueryable());
            // Sin envíos configurados

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.IsTrue(candidatos.Single().Preseleccionado);
        }

        [TestMethod]
        public async Task CandidatosSepa_ClienteConAbonosPendientes_MarcaLaPuertaDeNeteo()
        {
            // #332: el usuario debe pasar por la revisión (liquidar con #333) antes de remesar
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, cliente: "15191"),
                Efecto(id: 2, cliente: "30676"),
                // Abono pendiente del 15191 (no candidato por ser negativo, pero activa el flag)
                Efecto(id: 3, cliente: "15191", pendiente: -80m, tipoApunte: "1", ccc: null)
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.AreEqual(2, candidatos.Count);
            Assert.IsTrue(candidatos.Single(c => c.Cliente == "15191").ClienteConNegativos);
            Assert.IsFalse(candidatos.Single(c => c.Cliente == "30676").ClienteConNegativos);
        }

        // NestoAPI#381: la validación del IBAN se adelanta a la selección. Antes el error
        // "El cliente X no tiene un IBAN correcto" saltaba al GENERAR EL FICHERO, con la
        // remesa ya creada y contabilizada (caso real cliente 14986, 07/08/26).

        [TestMethod]
        public async Task CandidatosSepa_CccSinFichaBancaria_RetenidoConMotivo()
        {
            // El INNER JOIN del SP descartaría este efecto EN SILENCIO del fichero SEPA
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, ccc: "9"),   // el cliente 15191 solo tiene la ficha "1"
                Efecto(id: 2, ccc: "1")
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            EfectoCandidatoDTO retenido = candidatos.Single(c => c.Id == 1);
            Assert.IsFalse(retenido.Preseleccionado);
            StringAssert.Contains(retenido.Motivo, "no existe en la ficha bancaria");
            Assert.IsTrue(candidatos.Single(c => c.Id == 2).Preseleccionado);
        }

        [TestMethod]
        public async Task CandidatosSepa_IbanConComponenteNulo_RetenidoConMotivo()
        {
            // Regla EXACTA del SP: pais+dc_iban+entidad+oficina+dc+cuenta con algún NULL
            // da IBAN NULL → raiserror "no tiene un IBAN correcto" al crear el fichero
            ConfigurarFakeDbSet(fakeCccs, new List<CCC>
            {
                Ficha("15191", dc: null),
                Ficha("30676")
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, cliente: "15191"),
                Efecto(id: 2, cliente: "30676")
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            EfectoCandidatoDTO retenido = candidatos.Single(c => c.Id == 1);
            Assert.IsFalse(retenido.Preseleccionado);
            StringAssert.Contains(retenido.Motivo, "incompleto");
            Assert.IsTrue(candidatos.Single(c => c.Id == 2).Preseleccionado);
        }

        [TestMethod]
        public async Task CandidatosSepa_IbanCompletoPeroInvalido_RetenidoConMotivo()
        {
            // Un IBAN completo pero con mod-97 incorrecto pasaría el SP y lo rechazaría el
            // banco: también se retiene (validador exacto)
            ConfigurarFakeDbSet(fakeCccs, new List<CCC>
            {
                Ficha("15191", dcIban: "00")   // dígitos de control incorrectos
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto() }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            EfectoCandidatoDTO retenido = candidatos.Single();
            Assert.IsFalse(retenido.Preseleccionado);
            StringAssert.Contains(retenido.Motivo, "no es válido");
        }

        [TestMethod]
        public void MotivoRetencionIban_IbanValido_DevuelveNull()
        {
            Assert.IsNull(SelectorEfectosCobrables.MotivoRetencionIban(Ficha(), "1"));
        }
    }
}
