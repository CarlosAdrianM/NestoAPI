using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using NestoAPI.Tests.Infrastructure.Stocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using S = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>Sugerencia 1340: el modo de servicio 6, «Junto con lo pendiente del cliente».</summary>
    [TestClass]
    public class JuntoConPendienteTests
    {
        internal const string CLIENTE = "8118";
        internal const string CONTACTO = "0";
        internal const int PEDIDO = 931030;
        internal const int NOTA = 929701;

        internal static PendienteDelCliente Nota(int numero = NOTA, string contacto = CONTACTO, byte? modo = S.TODO_JUNTO, bool servirJunto = true,
            decimal basePendiente = 40, bool llevaPortes = false)
            => new PendienteDelCliente
            {
                Empresa = "1",
                Pedido = numero,
                Cliente = CLIENTE,
                Contacto = contacto,
                EsNotaEntrega = true,
                ModoServicio = modo,
                ServirJunto = servirJunto,
                BasePendiente = basePendiente,
                LlevaPortes = llevaPortes,
                Lineas = new List<LineaPendienteDelCliente>
                {
                    new LineaPendienteDelCliente { Producto = "45238", Texto = "CONJUNTO DUPLO STARPIL BASIC", Cantidad = 1, FechaEntrega = new DateTime(2099, 12, 31) }
                }
            };

        // --- Constantes: el 6 refina el «todo junto» ---

        [TestMethod]
        public void Efectivo_ConServirJunto_EsEl6()
        {
            Assert.AreEqual(S.JUNTO_CON_LO_PENDIENTE, S.Efectivo(S.JUNTO_CON_LO_PENDIENTE, true));
        }

        [TestMethod]
        public void Efectivo_SinServirJunto_DejaDeSer6()
        {
            // El Nesto viejo que desmarca «Servir junto» lo pasa a «Según vaya entrando», como a un 1.
            Assert.AreEqual(S.SEGUN_VAYA_ENTRANDO, S.Efectivo(S.JUNTO_CON_LO_PENDIENTE, false));
        }

        [TestMethod]
        public void Normalizar_Con6_ServirJuntoPasaATrue()
        {
            var pedido = new PedidoVentaDTO { modoServicio = S.JUNTO_CON_LO_PENDIENTE, servirJunto = false };

            Assert.IsNull(S.Normalizar(pedido, S.TODO_JUNTO));
            Assert.IsTrue(pedido.servirJunto);
            Assert.AreEqual(S.JUNTO_CON_LO_PENDIENTE, pedido.modoServicio);
        }

        [TestMethod]
        public void Normalizar_ClienteViejoSinModo_ConservaEl6Guardado()
        {
            // NestoApp o la tienda solo mandan el bool: un pedido en 6 no pierde su modo.
            var pedido = new PedidoVentaDTO { modoServicio = null, servirJunto = true };

            Assert.IsNull(S.Normalizar(pedido, S.JUNTO_CON_LO_PENDIENTE));
            Assert.AreEqual(S.JUNTO_CON_LO_PENDIENTE, pedido.modoServicio);
        }

        [TestMethod]
        public void Modo6_EsTodoJuntoYEntregaUnica_PeroNoValeComoParametroPorDefecto()
        {
            Assert.IsTrue(S.EsTodoJunto(S.JUNTO_CON_LO_PENDIENTE));
            Assert.IsTrue(S.EsEntregaUnica(S.JUNTO_CON_LO_PENDIENTE));
            Assert.IsFalse(S.EsValido(S.JUNTO_CON_LO_PENDIENTE));
            Assert.IsTrue(S.EsElegible(S.JUNTO_CON_LO_PENDIENTE));
            Assert.IsNull(S.ParsearModoForzado("6"));
            Assert.AreEqual(S.POR_DEFECTO, S.ParsearPorDefecto("6"));
            Assert.AreEqual("Junto con lo pendiente del cliente", S.Nombre(S.JUNTO_CON_LO_PENDIENTE));
        }

        [TestMethod]
        public void Normalizar_Modo7_NoExiste()
        {
            var pedido = new PedidoVentaDTO { modoServicio = 7 };

            StringAssert.Contains(S.Normalizar(pedido, null), "no existe");
        }

        [TestMethod]
        public void NotaHeredadaDeUnPedidoEnModo6_VaTodoJunto()
        {
            byte modo = CreadorNotaEntregaPendiente.ModoServicioHeredado(new CabPedidoVta { ModoServicio = S.JUNTO_CON_LO_PENDIENTE, ServirJunto = true });

            Assert.AreEqual(S.TODO_JUNTO, modo);
        }

        [TestMethod]
        public void Facturacion_Modo6_NoSeTrocea()
        {
            Assert.IsFalse(GestorPresupuestos.SeTroceaLaFacturacion(S.JUNTO_CON_LO_PENDIENTE, faltaStockDeAlgo: true));
        }

        // --- La regla: con qué se junta ---

        [TestMethod]
        public void Cuenta_NotaYPedidoDelMismoClienteYContacto()
        {
            PendienteDelCliente pedidoNormal = Nota(931020);
            pedidoNormal.EsNotaEntrega = false;
            pedidoNormal.ModoServicio = S.SEGUN_VAYA_ENTRANDO;
            pedidoNormal.ServirJunto = false;

            List<PendienteDelCliente> conLoQue = ReglaJuntoConPendiente.ConLoQueSeJunta(new[] { pedidoNormal, Nota() }, "1", PEDIDO, CLIENTE, CONTACTO);

            CollectionAssert.AreEqual(new[] { NOTA, 931020 }, conLoQue.Select(p => p.Pedido).ToArray());
        }

        [TestMethod]
        public void NoCuentan_ElPropio_OtraDireccion_OtraEmpresa_Retenidos_OtrosModo6_NiSinLineas()
        {
            PendienteDelCliente otraEmpresa = Nota(900003);
            otraEmpresa.Empresa = "3";
            PendienteDelCliente sinLineas = Nota(900006);
            sinLineas.Lineas.Clear();
            var pendientes = new[]
            {
                Nota(PEDIDO),
                Nota(900001, contacto: "1"),
                otraEmpresa,
                Nota(900004, modo: S.RETENIDO),
                Nota(900005, modo: S.JUNTO_CON_LO_PENDIENTE),
                sinLineas
            };

            Assert.AreEqual(0, ReglaJuntoConPendiente.ConLoQueSeJunta(pendientes, "1", PEDIDO, CLIENTE, CONTACTO).Count);
        }

        [TestMethod]
        public void DosPedidosEnModo6_NoSeEsperanElUnoAlOtro()
        {
            // El 931030 (modo 6) y el 931031 (modo 6) del mismo cliente: ninguno espera al otro; los dos esperan a la nota.
            var pendientes = new[] { Nota(), Nota(931031, modo: S.JUNTO_CON_LO_PENDIENTE) };

            CollectionAssert.AreEqual(new[] { NOTA }, ReglaJuntoConPendiente.ConLoQueSeJunta(pendientes, "1", PEDIDO, CLIENTE, CONTACTO).Select(p => p.Pedido).ToArray());
            CollectionAssert.AreEqual(new[] { NOTA }, ReglaJuntoConPendiente.ConLoQueSeJunta(pendientes, "1", 931031, CLIENTE, CONTACTO).Select(p => p.Pedido).ToArray());
        }

        [TestMethod]
        public void Cuenta_ClienteYContactoConEspaciosDeRelleno()
        {
            Assert.IsTrue(ReglaJuntoConPendiente.Cuenta(Nota(), "1  ", PEDIDO, CLIENTE + "      ", CONTACTO + "  "));
        }

        [TestMethod]
        public void Explicacion_UnoYVarios()
        {
            PendienteDelCliente pedido = Nota(931020);
            pedido.EsNotaEntrega = false;

            Assert.AreEqual("Saldrá junto con la nota de entrega 929701.", ReglaJuntoConPendiente.Explicacion(new[] { Nota() }));
            Assert.AreEqual("Saldrá junto con la nota de entrega 929701 y el pedido 931020.", ReglaJuntoConPendiente.Explicacion(new[] { Nota(), pedido }));
            Assert.AreEqual(ReglaJuntoConPendiente.EXPLICACION_NADA_PENDIENTE, ReglaJuntoConPendiente.Explicacion(null));
        }

        [TestMethod]
        public void Opcion_SoloSiHayAlgoPendienteOYaEstaEn6_YNuncaEnTienda()
        {
            Assert.IsNull(ReglaJuntoConPendiente.Opcion(new PendienteDelCliente[0], false, ModosServicioPermitidos.TipoAlmacen.Central));
            Assert.IsNull(ReglaJuntoConPendiente.Opcion(new[] { Nota() }, false, ModosServicioPermitidos.TipoAlmacen.Tienda));

            ModoServicioPermitidoDTO conNota = ReglaJuntoConPendiente.Opcion(new[] { Nota() }, false, ModosServicioPermitidos.TipoAlmacen.Central);
            Assert.AreEqual(S.JUNTO_CON_LO_PENDIENTE, conNota.Modo);
            Assert.IsTrue(conNota.Permitido);
            Assert.AreEqual("Saldrá junto con la nota de entrega 929701.", conNota.Explicacion);
            CollectionAssert.AreEqual(new[] { NOTA }, conNota.PedidosConLosQueSeJunta);

            ModoServicioPermitidoDTO yaEn6 = ReglaJuntoConPendiente.Opcion(new PendienteDelCliente[0], true, ModosServicioPermitidos.TipoAlmacen.Central);
            Assert.AreEqual(ReglaJuntoConPendiente.EXPLICACION_NADA_PENDIENTE, yaEn6.Explicacion);
        }

        [TestMethod]
        public void ConModoJunto_AnadeEl6SinTocarLaSugerenciaOriginalNiElModoSugerido()
        {
            var original = new SugeridorModoServicio.Sugerencia
            {
                Modo = S.TODO_JUNTO,
                ModosPermitidos = new List<byte> { S.TODO_JUNTO },
                Modos = ModosServicioPermitidos.Calcular(ModosServicioPermitidos.TipoAlmacen.Central, 2, 0, 0, true)
            };
            ModoServicioPermitidoDTO opcion = ReglaJuntoConPendiente.Opcion(new[] { Nota() }, false, ModosServicioPermitidos.TipoAlmacen.Central);

            SugeridorModoServicio.Sugerencia copia = ReglaJuntoConPendiente.ConModoJunto(original, opcion);

            Assert.AreEqual(S.TODO_JUNTO, copia.Modo, "Nunca se sugiere el 6");
            CollectionAssert.AreEqual(new List<byte> { S.TODO_JUNTO, S.JUNTO_CON_LO_PENDIENTE }, copia.ModosPermitidos);
            Assert.AreEqual(5, copia.Modos.Count);
            Assert.AreEqual(1, original.ModosPermitidos.Count, "La original (de la caché) no se toca");
            Assert.AreEqual(4, original.Modos.Count);
            Assert.AreSame(original, ReglaJuntoConPendiente.ConModoJunto(original, null), "Sin opción, la misma");
        }

        [TestMethod]
        public void MotivoNoSePuedeElegir_SinScriptOSinNadaPendiente()
        {
            Assert.AreEqual(ReglaJuntoConPendiente.MOTIVO_SIN_SCRIPT, ReglaJuntoConPendiente.MotivoNoSePuedeElegir(false, 3));
            Assert.AreEqual(ReglaJuntoConPendiente.MOTIVO_NADA_PENDIENTE, ReglaJuntoConPendiente.MotivoNoSePuedeElegir(true, 0));
            Assert.IsNull(ReglaJuntoConPendiente.MotivoNoSePuedeElegir(true, 1));
        }

        [TestMethod]
        public void Portes_SumaLaBaseYMiraSiLoPendienteYaLosLleva()
        {
            var pendientes = new[] { Nota(basePendiente: 40), Nota(931020, basePendiente: 25.5M, llevaPortes: true) };

            Assert.AreEqual(65.5M, ReglaJuntoConPendiente.BaseDeLoPendiente(pendientes));
            Assert.IsTrue(ReglaJuntoConPendiente.LoPendienteYaLlevaPortes(pendientes));
            Assert.IsFalse(ReglaJuntoConPendiente.LoPendienteYaLlevaPortes(new[] { Nota() }));
        }

        // --- La lectura agrupada ---

        [TestMethod]
        public void Agrupar_SoloProductoDeAlgeteSinPickingYConCantidad_YVeLosPortes()
        {
            var filas = new List<RepositorioJuntoConPendiente.FilaPendiente>
            {
                Fila(NOTA, Constantes.TiposLineaVenta.PRODUCTO, "45238", 1, "ALG", null, 30M),
                Fila(NOTA, Constantes.TiposLineaVenta.PRODUCTO, "11111", 2, "REI", null, 10M),
                Fila(NOTA, Constantes.TiposLineaVenta.PRODUCTO, "22222", 1, "ALG", 555, 10M),
                Fila(NOTA, Constantes.TiposLineaVenta.CUENTA_CONTABLE, "62400003", 1, "ALG", null, 5M, "Portes"),
                // Una nota de recogida: solo texto, no cuenta
                Fila(900010, Constantes.TiposLineaVenta.TEXTO, null, 1, "ALG", null, 0M, "Recoger caja"),
                // Todo ya en picking: está saliendo, no se espera
                Fila(900011, Constantes.TiposLineaVenta.PRODUCTO, "33333", 1, "ALG", 556, 10M)
            };

            List<PendienteDelCliente> pendientes = RepositorioJuntoConPendiente.Agrupar("1", filas);

            Assert.AreEqual(1, pendientes.Count);
            Assert.AreEqual(NOTA, pendientes[0].Pedido);
            Assert.AreEqual(30M, pendientes[0].BasePendiente);
            Assert.IsTrue(pendientes[0].LlevaPortes);
            Assert.AreEqual("45238", pendientes[0].Lineas.Single().Producto);
            Assert.AreEqual("CONJUNTO DUPLO STARPIL BASIC", pendientes[0].Lineas.Single().Texto);
        }

        private static RepositorioJuntoConPendiente.FilaPendiente Fila(int pedido, int tipo, string producto, short cantidad, string almacen, int? picking,
            decimal baseImponible, string texto = "CONJUNTO DUPLO STARPIL BASIC")
            => new RepositorioJuntoConPendiente.FilaPendiente
            {
                Pedido = pedido,
                TipoLinea = (byte)tipo,
                Producto = producto,
                Texto = texto,
                Cantidad = cantidad,
                Almacen = almacen,
                Picking = picking,
                BaseImponible = baseImponible,
                Estado = Constantes.EstadosLineaVenta.PENDIENTE,
                Cliente = CLIENTE + "      ",
                Contacto = CONTACTO + "  ",
                NotaEntrega = true,
                ModoServicio = S.TODO_JUNTO,
                ServirJunto = true,
                FechaEntrega = new DateTime(2099, 12, 31)
            };
    }

    /// <summary>Sugerencia 1340: ofrecer el 6 y validarlo al guardar.</summary>
    [TestClass]
    public class ServicioJuntoConPendienteTests
    {
        private IRepositorioJuntoConPendiente repositorio;
        private ServicioJuntoConPendiente servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioJuntoConPendiente>();
            A.CallTo(() => repositorio.ModoAdmitido()).Returns(true);
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._))
                .Returns(new List<PendienteDelCliente> { JuntoConPendienteTests.Nota() });
            servicio = new ServicioJuntoConPendiente(repositorio, _ => { });
        }

        private static PedidoVentaDTO Pedido(byte? modo = null, int numero = 0) => new PedidoVentaDTO
        {
            empresa = "1",
            numero = numero,
            cliente = JuntoConPendienteTests.CLIENTE,
            contacto = JuntoConPendienteTests.CONTACTO,
            modoServicio = modo,
            servirJunto = modo == S.JUNTO_CON_LO_PENDIENTE,
            Lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { Producto = "17404", Cantidad = 1, almacen = "ALG", tipoLinea = 1, PrecioUnitario = 20 }
            }
        };

        private static SugeridorModoServicio.Sugerencia Sugerencia() => new SugeridorModoServicio.Sugerencia
        {
            Modo = S.TODO_JUNTO,
            ModosPermitidos = new List<byte> { S.TODO_JUNTO },
            Modos = ModosServicioPermitidos.Calcular(ModosServicioPermitidos.TipoAlmacen.Central, 1, 0, 0, true)
        };

        [TestMethod]
        public void Ofrecer_ConNotaPendiente_AnadeEl6ConSuExplicacion()
        {
            SugeridorModoServicio.Sugerencia s = servicio.Ofrecer(Sugerencia(), Pedido(), false);

            ModoServicioPermitidoDTO seis = s.Modos.Single(m => m.Modo == S.JUNTO_CON_LO_PENDIENTE);
            Assert.AreEqual("Saldrá junto con la nota de entrega 929701.", seis.Explicacion);
            CollectionAssert.Contains(s.ModosPermitidos, S.JUNTO_CON_LO_PENDIENTE);
        }

        [TestMethod]
        public void Ofrecer_SinScript_NiSiquieraLee()
        {
            A.CallTo(() => repositorio.ModoAdmitido()).Returns(false);

            SugeridorModoServicio.Sugerencia s = servicio.Ofrecer(Sugerencia(), Pedido(), false);

            Assert.IsFalse(s.Modos.Any(m => m.Modo == S.JUNTO_CON_LO_PENDIENTE));
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Ofrecer_SinNadaPendiente_NoSale()
        {
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._)).Returns(new List<PendienteDelCliente>());

            Assert.IsFalse(servicio.Ofrecer(Sugerencia(), Pedido(), false).Modos.Any(m => m.Modo == S.JUNTO_CON_LO_PENDIENTE));
        }

        [TestMethod]
        public void Ofrecer_SiFallaLaLectura_LaSugerenciaDeSiempre()
        {
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._)).Throws(new Exception("BD caída"));
            SugeridorModoServicio.Sugerencia original = Sugerencia();

            Assert.AreSame(original, servicio.Ofrecer(original, Pedido(), false));
        }

        [TestMethod]
        public void MotivoAlEntrar_Sin6_NiMira()
        {
            Assert.IsNull(servicio.MotivoAlEntrar(Pedido(S.TODO_JUNTO), S.TODO_JUNTO, null));
            A.CallTo(() => repositorio.ModoAdmitido()).MustNotHaveHappened();
        }

        [TestMethod]
        public void MotivoAlEntrar_Al6ConNota_Vale_SinNada_400()
        {
            Assert.IsNull(servicio.MotivoAlEntrar(Pedido(S.JUNTO_CON_LO_PENDIENTE), S.JUNTO_CON_LO_PENDIENTE, S.TODO_JUNTO));

            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._)).Returns(new List<PendienteDelCliente>());
            Assert.AreEqual(ReglaJuntoConPendiente.MOTIVO_NADA_PENDIENTE, servicio.MotivoAlEntrar(Pedido(S.JUNTO_CON_LO_PENDIENTE), S.JUNTO_CON_LO_PENDIENTE, S.TODO_JUNTO));
        }

        [TestMethod]
        public void MotivoAlEntrar_QuienYaEstabaEn6_NoSeRechazaAunqueNoQuedeNada()
        {
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._)).Returns(new List<PendienteDelCliente>());

            Assert.IsNull(servicio.MotivoAlEntrar(Pedido(S.JUNTO_CON_LO_PENDIENTE, 931030), S.JUNTO_CON_LO_PENDIENTE, S.JUNTO_CON_LO_PENDIENTE));
        }

        [TestMethod]
        public void MotivoAlEntrar_SinScript_400()
        {
            A.CallTo(() => repositorio.ModoAdmitido()).Returns(false);

            Assert.AreEqual(ReglaJuntoConPendiente.MOTIVO_SIN_SCRIPT, servicio.MotivoAlEntrar(Pedido(S.JUNTO_CON_LO_PENDIENTE), S.JUNTO_CON_LO_PENDIENTE, null));
        }

        [TestMethod]
        public void PortesDeLoPendiente_SoloEnModo6()
        {
            Assert.AreEqual((0M, false), servicio.PortesDeLoPendiente(Pedido(S.TODO_JUNTO)));
            Assert.AreEqual((40M, false), servicio.PortesDeLoPendiente(Pedido(S.JUNTO_CON_LO_PENDIENTE)));
        }

        [TestMethod]
        public void ValidadorModoServicio_6EnAlgete_VaSiVaElTodoJunto()
        {
            IGestorStocks stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._, A<int>._)).Returns(SugeridorModoServicio.VERDE);
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._)).Returns(SugeridorModoServicio.VERDE);

            // Todo verde: solo «Todo junto» está permitido, y el 6 (que sale entero) también vale.
            Assert.IsNull(ValidadorModoServicio.ComprobarAlCrear(Pedido(S.JUNTO_CON_LO_PENDIENTE), stocks));
        }

        [TestMethod]
        public void ValidadorModoServicio_6EnTienda_SeCorrigeASegunVayaEntrando()
        {
            PedidoVentaDTO pedido = Pedido(S.JUNTO_CON_LO_PENDIENTE);
            pedido.Lineas.First().almacen = "ALC";

            Assert.AreEqual(S.SEGUN_VAYA_ENTRANDO, ValidadorModoServicio.ComprobarAlCrear(pedido, A.Fake<IGestorStocks>()));
        }
    }

    /// <summary>Sugerencia 1340: el 6 en el picking (esperas y portes del grupo).</summary>
    [TestClass]
    public class JuntoConPendientePickingTests
    {
        private static PedidoPicking Pedido(int id, byte modo, decimal baseImponible = 50, params int[] juntoCon)
        {
            var pedido = new PedidoPicking(A.Fake<IRellenadorPrepagosService>())
            {
                Empresa = "1",
                Id = id,
                Cliente = JuntoConPendienteTests.CLIENTE,
                Contacto = JuntoConPendienteTests.CONTACTO,
                ModoServicio = modo,
                ServirJunto = S.EsTodoJunto(modo),
                Ruta = "FW",
                Iva = "G21",
                Lineas = new List<LineaPedidoPicking>
                {
                    new LineaPedidoPicking { Id = 1, NumeroPedido = id, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "P" + id, Almacen = "ALG",
                        Cantidad = 1, CantidadReservada = 1, BaseImponible = baseImponible, Total = baseImponible * 1.21M }
                },
                JuntoCon = juntoCon.Select(n => JuntoConPendienteTests.Nota(n)).ToList()
            };
            return pedido;
        }

        [TestMethod]
        public void SinNadaQueEsperar_SeDecideComoUnTodoJunto()
        {
            PedidoPicking seis = Pedido(931030, S.JUNTO_CON_LO_PENDIENTE);

            Assert.IsFalse(JuntoConPendientePicking.EsperaAlgo(seis));
            JuntoConPendientePicking.MarcarEsperasIniciales(new[] { seis });
            Assert.IsTrue(seis.saleEnPicking());
            Assert.IsTrue(seis.ExigeStockDeTodo());
        }

        [TestMethod]
        public void LoPendienteNoEstaEnLaPasada_Espera()
        {
            PedidoPicking seis = Pedido(931030, S.JUNTO_CON_LO_PENDIENTE, 50, JuntoConPendienteTests.NOTA);

            JuntoConPendientePicking.MarcarEsperasIniciales(new[] { seis });

            CollectionAssert.AreEqual(new[] { JuntoConPendienteTests.NOTA }, seis.EsperandoA);
            Assert.IsFalse(seis.saleEnPicking());
            Assert.AreEqual(MotivoNoSalePicking.EsperaPendienteDelCliente, seis.MotivoNoSale);
            StringAssert.Contains(ExplicadorPedidoSinPicking.TextoEsperaPendienteDelCliente(seis), "espera a la nota de entrega 929701, que no sale en este picking");
        }

        [TestMethod]
        public void LoPendienteEstaYSale_SalenJuntos_SiNoSale_Espera()
        {
            PedidoPicking nota = Pedido(JuntoConPendienteTests.NOTA, S.TODO_JUNTO);
            PedidoPicking seis = Pedido(931030, S.JUNTO_CON_LO_PENDIENTE, 50, JuntoConPendienteTests.NOTA);
            var candidatos = new List<PedidoPicking> { nota, seis };

            JuntoConPendientePicking.MarcarEsperasIniciales(candidatos);
            Assert.IsFalse(seis.EsperandoA.Any(), "La nota está en la pasada: aún no se sabe si sale");
            Assert.IsTrue(JuntoConPendientePicking.PuedeIrConUnModo6(nota, candidatos));

            JuntoConPendientePicking.MarcarEsperasFinales(seis, candidatos);
            Assert.IsTrue(seis.saleEnPicking(), "La nota sale: el 6 sale con ella");

            nota.Borrar = true;
            JuntoConPendientePicking.MarcarEsperasFinales(seis, candidatos);
            Assert.IsFalse(seis.saleEnPicking(), "La nota no sale: el 6 espera");
        }

        [TestMethod]
        public void Portes_ElGrupoSumaYPagaUnaVez()
        {
            // 50 + 40 = 90 ≥ 75 (ruta FW): ninguno paga portes, aunque cada uno por separado no llegaría.
            PedidoPicking nota = Pedido(JuntoConPendienteTests.NOTA, S.TODO_JUNTO, 40);
            PedidoPicking seis = Pedido(931030, S.JUNTO_CON_LO_PENDIENTE, 50, JuntoConPendienteTests.NOTA);
            var candidatos = new List<PedidoPicking> { nota, seis };
            var conPortes = new List<int>();

            JuntoConPendientePicking.PortesDeLosAplazados(new List<PedidoPicking> { nota, seis }, candidatos, p => conPortes.Add(p.Id),
                p => !new GestorImportesMinimos(p).LaEntregaLlegaAlImporteMinimo() && !p.PortesYaEnElGrupo);

            Assert.AreEqual(0, conPortes.Count);
            Assert.AreEqual(50M, nota.ImporteEntregaJunto);
            Assert.AreEqual(40M, seis.ImporteEntregaJunto);
        }

        [TestMethod]
        public void Portes_SiNoLlegan_SeCobranUnaSolaVez()
        {
            PedidoPicking nota = Pedido(JuntoConPendienteTests.NOTA, S.TODO_JUNTO, 20);
            PedidoPicking seis = Pedido(931030, S.JUNTO_CON_LO_PENDIENTE, 30, JuntoConPendienteTests.NOTA);
            var candidatos = new List<PedidoPicking> { nota, seis };
            var conPortes = new List<int>();

            JuntoConPendientePicking.PortesDeLosAplazados(new List<PedidoPicking> { nota, seis }, candidatos, p => conPortes.Add(p.Id),
                p => !new GestorImportesMinimos(p).LaEntregaLlegaAlImporteMinimo() && !p.PortesYaEnElGrupo);

            CollectionAssert.AreEqual(new[] { JuntoConPendienteTests.NOTA }, conPortes);
        }

        [TestMethod]
        public void Portes_SiLoPendienteYaLosLleva_NoSeDuplican()
        {
            PedidoPicking nota = Pedido(JuntoConPendienteTests.NOTA, S.TODO_JUNTO, 20);
            nota.Lineas.Add(new LineaPedidoPicking { Id = 2, TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE, Producto = "62400003", Cantidad = 1, CantidadReservada = 1 });
            PedidoPicking seis = Pedido(931030, S.JUNTO_CON_LO_PENDIENTE, 30, JuntoConPendienteTests.NOTA);
            var conPortes = new List<int>();

            JuntoConPendientePicking.PortesDeLosAplazados(new List<PedidoPicking> { nota, seis }, new List<PedidoPicking> { nota, seis }, p => conPortes.Add(p.Id),
                p => !p.PortesYaEnElGrupo);

            Assert.AreEqual(0, conPortes.Count);
        }

        [TestMethod]
        public void Portes_SiEl6NoSale_CadaUnoLosSuyos()
        {
            PedidoPicking nota = Pedido(JuntoConPendienteTests.NOTA, S.TODO_JUNTO, 20);
            PedidoPicking seis = Pedido(931030, S.JUNTO_CON_LO_PENDIENTE, 30, JuntoConPendienteTests.NOTA);
            seis.Borrar = true;
            var conPortes = new List<int>();

            JuntoConPendientePicking.PortesDeLosAplazados(new List<PedidoPicking> { nota }, new List<PedidoPicking> { nota, seis }, p => conPortes.Add(p.Id),
                p => !new GestorImportesMinimos(p).LaEntregaLlegaAlImporteMinimo());

            CollectionAssert.AreEqual(new[] { JuntoConPendienteTests.NOTA }, conPortes);
            Assert.AreEqual(0M, nota.ImporteEntregaJunto);
        }
    }

    /// <summary>Sugerencia 1340: la fecha que se dice al cliente (decisión 6).</summary>
    [TestClass]
    public class ReglaFechaJuntoConPendienteTests
    {
        private static readonly DateTime JUEVES = new DateTime(2026, 11, 19);
        private static readonly DateTime LUNES_30 = new DateTime(2026, 11, 30);

        private static FechaEntregaAgenciaDTO Propio(DateTime? fecha) => new FechaEntregaAgenciaDTO
        {
            FechaEntregaAgencia = fecha,
            EntregaCompleta = fecha,
            PrimeraEntrega = fecha,
            Aplica = FechaEntregaAgenciaDTO.APLICA_COMPLETA,
            Motivo = "Modo «Todo junto»: sale cuando esté todo.",
            Desglose = new List<PartidaFechaEntregaAgenciaDTO>
            {
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, Producto = "17404", Unidades = 1, PuedeSalirEl = fecha }
            }
        };

        private static CalculoPendienteDelCliente DeLaNota(DateTime? fecha) => new CalculoPendienteDelCliente
        {
            Pendiente = JuntoConPendienteTests.Nota(),
            Calculo = new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = fecha,
                Desglose = new List<PartidaFechaEntregaAgenciaDTO>
                {
                    new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, Producto = "45238", Unidades = 1, PuedeSalirEl = fecha,
                        Certeza = PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA }
                }
            }
        };

        [TestMethod]
        public void LaNotaVaDespues_SaleElDiaDeLaNotaYSeDicePorQue()
        {
            FechaEntregaAgenciaDTO r = ReglaFechaJuntoConPendiente.Combinar(Propio(JUEVES), new[] { DeLaNota(LUNES_30) });

            Assert.AreEqual(LUNES_30, r.FechaEntregaAgencia);
            Assert.AreEqual(LUNES_30, r.EntregaCompleta);
            Assert.AreEqual(FechaEntregaAgenciaDTO.APLICA_COMPLETA, r.Aplica);
            Assert.IsTrue(r.JuntoConPendiente.LoRetrasaLoPendiente);
            Assert.AreEqual(JUEVES, r.JuntoConPendiente.FechaPropia);
            Assert.AreEqual("Sale el lunes 30/11/2026, junto con la nota de entrega 929701, que espera el CONJUNTO DUPLO STARPIL BASIC (45238).",
                r.JuntoConPendiente.Texto);
            StringAssert.StartsWith(r.Motivo, "Modo «Junto con lo pendiente del cliente»: sale cuando esté todo.");
            Assert.AreEqual(1, r.Desglose.Count, "El desglose sigue siendo solo el del pedido");
        }

        [TestMethod]
        public void LaNotaSinFecha_ElPedidoTampocoTiene()
        {
            FechaEntregaAgenciaDTO r = ReglaFechaJuntoConPendiente.Combinar(Propio(JUEVES), new[] { DeLaNota(null) });

            Assert.IsNull(r.FechaEntregaAgencia);
            Assert.AreEqual("Saldrá junto con la nota de entrega 929701, que espera el CONJUNTO DUPLO STARPIL BASIC (45238) y todavía no tiene fecha.",
                r.JuntoConPendiente.Texto);
        }

        [TestMethod]
        public void ElPedidoVaDespues_LoPendientePuedeSalirAntes()
        {
            FechaEntregaAgenciaDTO r = ReglaFechaJuntoConPendiente.Combinar(Propio(LUNES_30), new[] { DeLaNota(JUEVES) });

            Assert.AreEqual(LUNES_30, r.FechaEntregaAgencia);
            Assert.IsFalse(r.JuntoConPendiente.LoRetrasaLoPendiente);
            StringAssert.Contains(r.JuntoConPendiente.Texto, "puede salir antes por su cuenta");
        }

        [TestMethod]
        public void MismoDia_SaleJunto()
        {
            FechaEntregaAgenciaDTO r = ReglaFechaJuntoConPendiente.Combinar(Propio(JUEVES), new[] { DeLaNota(JUEVES) });

            Assert.AreEqual("Sale el jueves 19/11/2026, junto con la nota de entrega 929701.", r.JuntoConPendiente.Texto);
        }

        [TestMethod]
        public void SinNadaPendiente_ComoTodoJunto()
        {
            FechaEntregaAgenciaDTO r = ReglaFechaJuntoConPendiente.Combinar(Propio(JUEVES), new CalculoPendienteDelCliente[0]);

            Assert.AreEqual(JUEVES, r.FechaEntregaAgencia);
            Assert.IsNull(r.JuntoConPendiente);
            StringAssert.EndsWith(r.Motivo, ReglaFechaJuntoConPendiente.MOTIVO_NADA_PENDIENTE);
        }

        [TestMethod]
        public void PromesaEnRiesgo_LoRetrasaLoPendiente_SeAvisaConSuFrase()
        {
            FechaEntregaAgenciaDTO r = ReglaFechaJuntoConPendiente.Combinar(Propio(JUEVES), new[] { DeLaNota(LUNES_30) });

            Assert.IsTrue(EvaluadorPromesaRiesgo.MereceAviso(JUEVES, r), "Tiene culpable (lo que espera la nota): aviso en el acto, no observación");
            Assert.AreEqual("Va junto con la nota de entrega 929701, que espera el 45238 CONJUNTO DUPLO STARPIL BASIC: sale el lunes 30/11 (aproximado).",
                TextosPromesaRiesgo.Motivo(JUEVES, r, null, new DateTime(2026, 11, 18)));
        }

        [TestMethod]
        public void Correo_LlevaLaFrase()
        {
            FechaEntregaAgenciaDTO r = ReglaFechaJuntoConPendiente.Combinar(Propio(JUEVES), new[] { DeLaNota(LUNES_30) });

            string html = GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(r, false, false, new List<LineaPedidoVentaDTO>(), 6);

            StringAssert.Contains(html, "junto con la nota de entrega 929701, que espera el CONJUNTO DUPLO STARPIL BASIC (45238).");
            StringAssert.Contains(html, "espera a lo pendiente del cliente para salir junto");
        }
    }

    /// <summary>Sugerencia 1340: de punta a punta con el servicio real del motor (repositorios falsos).</summary>
    [TestClass]
    public class FechaEntregaJuntoConPendienteTests
    {
        // Miércoles 18/11/2026 a las 10:30. Solo cierran sábados y domingos.
        private static readonly DateTime AHORA = new DateTime(2026, 11, 18, 10, 30, 0);

        private static bool EsFestivo(DateTime dia, string almacen) => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday;

        [TestMethod]
        public async Task PedidoEnModo6_SaleElDiaDeLaNotaQueEsperaAlProveedor()
        {
            ServicioFechaEntregaAgencia.OlvidarAvisos();
            IRepositorioFechaEntregaAgencia repositorio = A.Fake<IRepositorioFechaEntregaAgencia>();
            A.CallTo(() => repositorio.LeerCalendario(A<string>._)).Returns(Task.FromResult(new List<ReposicionCalendario>()));
            A.CallTo(() => repositorio.LeerHoraCorte(A<string>._)).Returns(new TimeSpan(11, 0, 0));
            var datos = new DatosSombraModoServicio();
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave("17404", "ALG"), 10);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave("17404"), 10);
            string clave = ResumenStocksProductos.Clave("45238", "ALG");
            datos.PendienteRecibir[clave] = 5;
            datos.FechaPrevista[clave] = new DateTime(2026, 11, 30);
            datos.PedidoCompraPrevisto[clave] = 220999;
            datos.EmpresaCompraPrevista[clave] = "1";
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(datos);
            A.CallTo(() => repositorio.LeerPedido("1", JuntoConPendienteTests.PEDIDO)).Returns(Task.FromResult(new PedidoFechaEntregaAgencia
            {
                Empresa = "1", Numero = JuntoConPendienteTests.PEDIDO, Ruta = "FW", ModoServicio = S.JUNTO_CON_LO_PENDIENTE, ServirJunto = true,
                Cliente = JuntoConPendienteTests.CLIENTE, Contacto = JuntoConPendienteTests.CONTACTO,
                Lineas = new List<LineaPedidoFechaEntregaAgencia> { new LineaPedidoFechaEntregaAgencia { Producto = "17404", Almacen = "ALG", Cantidad = 1, BaseImponible = 20 } }
            }));
            A.CallTo(() => repositorio.LeerPedido("1", JuntoConPendienteTests.NOTA)).Returns(Task.FromResult(new PedidoFechaEntregaAgencia
            {
                Empresa = "1", Numero = JuntoConPendienteTests.NOTA, Ruta = "FW", ModoServicio = S.TODO_JUNTO, ServirJunto = true,
                Cliente = JuntoConPendienteTests.CLIENTE, Contacto = JuntoConPendienteTests.CONTACTO,
                Lineas = new List<LineaPedidoFechaEntregaAgencia> { new LineaPedidoFechaEntregaAgencia { Producto = "45238", Almacen = "ALG", Cantidad = 1, BaseImponible = 40 } }
            }));
            IRepositorioJuntoConPendiente junto = A.Fake<IRepositorioJuntoConPendiente>();
            A.CallTo(() => junto.ModoAdmitido()).Returns(true);
            A.CallTo(() => junto.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._))
                .Returns(new List<PendienteDelCliente> { JuntoConPendienteTests.Nota() });
            var servicio = new ServicioFechaEntregaAgencia(repositorio, () => AHORA, EsFestivo, _ => { },
                repartoStock: RepartoStockFalso.DesdeSombra(repositorio), juntoConPendiente: junto);

            FechaEntregaAgenciaDTO solo = await servicio.CalcularPedido("1", JuntoConPendienteTests.NOTA);
            FechaEntregaAgenciaDTO dto = await servicio.CalcularPedido("1", JuntoConPendienteTests.PEDIDO);

            Assert.IsNotNull(solo.FechaEntregaAgencia, "La nota tiene fecha (llega del proveedor)");
            Assert.IsNull(solo.JuntoConPendiente, "La nota no está en modo 6");
            Assert.AreEqual(solo.FechaEntregaAgencia, dto.FechaEntregaAgencia, "El 6 sale con la nota");
            Assert.IsTrue(dto.JuntoConPendiente.LoRetrasaLoPendiente);
            StringAssert.Contains(dto.JuntoConPendiente.Texto, "junto con la nota de entrega 929701, que espera el CONJUNTO DUPLO STARPIL BASIC (45238).");
            Assert.IsTrue(dto.Desglose.All(p => p.Producto == "17404"), "El desglose es el del pedido");
        }
    }

    /// <summary>Sugerencia 1340: el aviso de los 15 días (decisión 5).</summary>
    [TestClass]
    public class AvisosJuntoConPendienteTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 12, 3, 9, 30, 0);

        [TestMethod]
        public async Task AvisaUnaVez_AQuienLoHizoYASuVendedor()
        {
            IRepositorioJuntoConPendiente repositorio = A.Fake<IRepositorioJuntoConPendiente>();
            A.CallTo(() => repositorio.LeerEsperandoSinAvisar(A<DateTime>._)).Returns(new List<PedidoEsperandoJunto>
            {
                new PedidoEsperandoJunto { Empresa = "1", Pedido = JuntoConPendienteTests.PEDIDO, Cliente = JuntoConPendienteTests.CLIENTE,
                    Contacto = JuntoConPendienteTests.CONTACTO, Usuario = "NUEVAVISION\\Paloma", Vendedor = "PA", Desde = new DateTime(2026, 11, 18, 12, 0, 0) }
            });
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._))
                .Returns(new List<PendienteDelCliente> { JuntoConPendienteTests.Nota() });
            A.CallTo(() => repositorio.MarcarAvisado("1", JuntoConPendienteTests.PEDIDO, AHORA)).Returns(true);
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var avisos = new AvisosJuntoConPendiente(repositorio, () => push, () => AHORA, _ => { });

            int avisados = await avisos.AvisarLosQueLlevanDemasiado();

            Assert.AreEqual(1, avisados);
            A.CallTo(() => repositorio.LeerEsperandoSinAvisar(new DateTime(2026, 11, 19))).MustHaveHappened();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Paloma", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisosJuntoConPendiente.TIPO && n.Cuerpo.Contains("lleva 15 días")
                    && n.Cuerpo.Contains("esperando a la nota de entrega 929701")))).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarAVendedor("1", "PA", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task SiYaNoEsperaANada_NoAvisaNiMarca()
        {
            IRepositorioJuntoConPendiente repositorio = A.Fake<IRepositorioJuntoConPendiente>();
            A.CallTo(() => repositorio.LeerEsperandoSinAvisar(A<DateTime>._)).Returns(new List<PedidoEsperandoJunto>
            {
                new PedidoEsperandoJunto { Empresa = "1", Pedido = JuntoConPendienteTests.PEDIDO, Cliente = JuntoConPendienteTests.CLIENTE,
                    Contacto = JuntoConPendienteTests.CONTACTO, Usuario = "paloma", Desde = AHORA.AddDays(-20) }
            });
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._)).Returns(new List<PendienteDelCliente>());
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();

            int avisados = await new AvisosJuntoConPendiente(repositorio, () => push, () => AHORA, _ => { }).AvisarLosQueLlevanDemasiado();

            Assert.AreEqual(0, avisados);
            A.CallTo(() => repositorio.MarcarAvisado(A<string>._, A<int>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(push).Where(c => c.Method.Name.StartsWith("Enviar") || c.Method.Name.StartsWith("Guardar")).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiOtraPasadaYaLoMarco_NoRepite()
        {
            IRepositorioJuntoConPendiente repositorio = A.Fake<IRepositorioJuntoConPendiente>();
            A.CallTo(() => repositorio.LeerEsperandoSinAvisar(A<DateTime>._)).Returns(new List<PedidoEsperandoJunto>
            {
                new PedidoEsperandoJunto { Empresa = "1", Pedido = JuntoConPendienteTests.PEDIDO, Cliente = JuntoConPendienteTests.CLIENTE,
                    Contacto = JuntoConPendienteTests.CONTACTO, Usuario = "paloma", Desde = AHORA.AddDays(-20) }
            });
            A.CallTo(() => repositorio.LeerPendientes(A<string>._, A<IEnumerable<ClaveClienteContacto>>._))
                .Returns(new List<PendienteDelCliente> { JuntoConPendienteTests.Nota() });
            A.CallTo(() => repositorio.MarcarAvisado(A<string>._, A<int>._, A<DateTime>._)).Returns(false);
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();

            int avisados = await new AvisosJuntoConPendiente(repositorio, () => push, () => AHORA, _ => { }).AvisarLosQueLlevanDemasiado();

            Assert.AreEqual(0, avisados);
            A.CallTo(() => push.EnviarAUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }
    }
}
