using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Calidad;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>NestoAPI#914 (Carlos 27/11/26, «avisar y dejar seguir»): la etiqueta del packing con productos sin leer en la mesa.</summary>
    [TestClass]
    public class PackingIncompletoEtiquetaTests
    {
        private const int PEDIDO = 926940;

        private static LineaPackingAlmacenDTO Linea(string producto, int cantidad, int metidas, int faltas = 0, string nombre = null) =>
            new LineaPackingAlmacenDTO { Producto = producto, Descripcion = nombre ?? "Producto " + producto, Cantidad = cantidad, Metidas = metidas, Faltas = faltas };

        private static EntregaPackingAlmacenDTO Entrega(params PedidoPackingAlmacenDTO[] pedidos) =>
            new EntregaPackingAlmacenDTO { Pedidos = pedidos.ToList() };

        private static PedidoPackingAlmacenDTO Pedido(int pedido, params LineaPackingAlmacenDTO[] lineas) =>
            new PedidoPackingAlmacenDTO { Pedido = pedido, Lineas = lineas.ToList() };

        // ------------------------------------------------------------ LA REGLA

        [TestMethod]
        public void SinLeer_EsperadoMenosMetidoMenosFaltas_PorProductoYEnTodaLaEntrega()
        {
            List<ProductoSinLeerPackingDTO> sinLeer = ReglaPackingIncompleto.SinLeer(Entrega(
                Pedido(PEDIDO, Linea("41262", 2, 1, nombre: "SYNERGY"), Linea("17404", 3, 2, faltas: 1), new LineaPackingAlmacenDTO { EsTexto = true, Cantidad = 1 }),
                Pedido(926941, Linea("12673", 1, 0, nombre: "ESPÁTULA"))));

            Assert.AreEqual(2, sinLeer.Count);
            Assert.AreEqual("41262", sinLeer[0].Producto);
            Assert.AreEqual(1, sinLeer[0].Cantidad);
            Assert.AreEqual("SYNERGY", sinLeer[0].Nombre);
            Assert.AreEqual(926941, sinLeer[1].Pedido);
        }

        [TestMethod]
        public void SinLeer_LoMetidoDeMasEnLaUltimaLineaCompensaPorProducto()
        {
            // El reparto de lo metido (ServicioPreparacionAlmacen) pone lo que sobra en la última línea del producto
            Assert.AreEqual(0, ReglaPackingIncompleto.SinLeer(Entrega(Pedido(PEDIDO, Linea("A", 2, 1), Linea("A", 1, 2)))).Count);
            Assert.IsNull(ReglaPackingIncompleto.SinLeer(null));
        }

        [TestMethod]
        public void Decidir_LasCuatroSalidas()
        {
            var falta = new List<ProductoSinLeerPackingDTO> { new ProductoSinLeerPackingDTO { Producto = "A", Cantidad = 1 } };
            Assert.AreEqual(DecisionPackingIncompleto.Imprimir, ReglaPackingIncompleto.Decidir(null, true, false));
            Assert.AreEqual(DecisionPackingIncompleto.Imprimir, ReglaPackingIncompleto.Decidir(new List<ProductoSinLeerPackingDTO>(), true, false));
            Assert.AreEqual(DecisionPackingIncompleto.Avisar, ReglaPackingIncompleto.Decidir(falta, true, false));
            Assert.AreEqual(DecisionPackingIncompleto.ImprimirConfirmado, ReglaPackingIncompleto.Decidir(falta, true, true));
            Assert.AreEqual(DecisionPackingIncompleto.ImprimirSinAviso, ReglaPackingIncompleto.Decidir(falta, false, false));
        }

        [TestMethod]
        public void Mensaje_DeUstedSinEnumerarLosProductos_SingularYPlural()
        {
            var dos = new List<ProductoSinLeerPackingDTO>
            {
                new ProductoSinLeerPackingDTO { Producto = "41262", Nombre = "SYNERGY", Cantidad = 1 },
                new ProductoSinLeerPackingDTO { Producto = "12673", Nombre = "ESPÁTULA", Cantidad = 1 }
            };
            Assert.AreEqual("Faltan 2 productos por leer en la mesa (2 unidades). ¿Imprimir la etiqueta igualmente?", ReglaPackingIncompleto.Mensaje(dos));
            Assert.AreEqual("Falta 1 producto por leer en la mesa (1 unidad). ¿Imprimir la etiqueta igualmente?",
                ReglaPackingIncompleto.Mensaje(dos.Take(1).ToList()));
            Assert.AreEqual("Falta 1 producto por leer en la mesa (3 unidades). ¿Imprimir la etiqueta igualmente?",
                ReglaPackingIncompleto.Mensaje(new List<ProductoSinLeerPackingDTO> { new ProductoSinLeerPackingDTO { Producto = "A", Cantidad = 3 } }));
        }

        [TestMethod]
        public void Conflicto_ElSobreDeLos409ConCodigo()
        {
            var sinLeer = new List<ProductoSinLeerPackingDTO> { new ProductoSinLeerPackingDTO { Producto = "41262", Nombre = "SYNERGY", Cantidad = 2, Pedido = PEDIDO } };

            JObject json = JObject.Parse(JsonConvert.SerializeObject(ReglaPackingIncompleto.Conflicto(sinLeer)));

            Assert.AreEqual("PACKING_INCOMPLETO", (string)json["Codigo"]);
            Assert.AreEqual("PACKING_INCOMPLETO", (string)json["error"]["code"]);
            Assert.AreEqual((string)json["Message"], (string)json["error"]["message"]);
            Assert.AreEqual((string)json["Message"], (string)json["Mensaje"]);
            Assert.AreEqual(1, (int)json["Detalles"]["Faltan"]);
            Assert.AreEqual(2, (int)json["Detalles"]["Unidades"]);
            Assert.AreEqual("SYNERGY", (string)json["Detalles"]["Productos"][0]["Nombre"]);
            Assert.AreEqual(2, (int)json["error"]["details"]["Productos"][0]["Cantidad"]);
        }

        // ------------------------------------------------------------ En la etiqueta

        private IPasosEtiquetaPacking pasos;
        private ServicioEtiquetaPacking servicio;
        private IPrincipal usuario;
        private List<Tuple<string, string, int?, object>> apuntados;

        [TestInitialize]
        public void Preparar()
        {
            pasos = A.Fake<IPasosEtiquetaPacking>();
            usuario = new GenericPrincipal(new GenericIdentity("Andre", "Bearer"), new[] { "Almacén" });
            apuntados = new List<Tuple<string, string, int?, object>>();
            servicio = new ServicioEtiquetaPacking(_ => pasos, A.Fake<ILectorParametrosUsuario>(), _ => { })
            {
                MotivoPedidoRetenido = (e, p, rp) => Task.FromResult<string>(null),
                ApuntarCalidad = (tipo, u, empresa, pedido, cliente, contacto, documento, imputado, detalle, importe) =>
                    apuntados.Add(Tuple.Create(tipo, u, pedido, detalle))
            };
            A.CallTo(() => pasos.LeerEnvioEnCurso(A<string>._, A<int>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => pasos.NombreAgencia(A<int>._)).Returns("CTT");
            A.CallTo(() => pasos.Tramitar(A<int>._)).Returns(PasoEtiquetaAgencia.Bien("CTT123", 2));
            A.CallTo(() => pasos.Imprimir(A<int>._, A<string>._)).Returns(PasoEtiquetaAgencia.Bien("CTT123", 2, "ZEBRA"));
            var envio = new EnviosAgencia { Numero = 5001, Empresa = "1", Agencia = Constantes.Agencias.AGENCIA_CTT, Pedido = PEDIDO, Bultos = 2, CodigoBarras = "",
                Cliente = "15191", Contacto = "0", Estado = Constantes.Agencias.ESTADO_EN_CURSO };
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).Returns(ResultadoEnvioDesdePedido.Creado(
                new EnvioDesdePedidoDTO { Envio = envio, Origen = PropuestaEnvioDTO.ORIGEN_NUEVO, Flujo = ServicioEnvioDesdePedido.FlujoDe(envio.Agencia) }));
        }

        private void EntregaCon(List<ProductoSinLeerPackingDTO> sinLeer) =>
            A.CallTo(() => pasos.LeerEntrega("1", PEDIDO)).Returns(new EntregaParaEtiqueta { Bultos = 2, SinLeer = sinLeer, Picking = 99633 });

        private static List<ProductoSinLeerPackingDTO> UnoSinLeer() =>
            new List<ProductoSinLeerPackingDTO> { new ProductoSinLeerPackingDTO { Producto = "41262", Nombre = "SYNERGY", Cantidad = 1, Pedido = PEDIDO } };

        [TestMethod]
        public async Task Incompleto_ConAvisoSinConfirmar_409YNiGrabaNiImprimeNiApunta()
        {
            EntregaCon(UnoSinLeer());

            ResultadoEtiquetaPacking r = await servicio.Imprimir(PEDIDO, new PeticionEtiquetaPackingDTO { Empresa = "1", Peso = 2m, AvisarPackingIncompleto = true }, usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.PackingIncompleto, r.Tipo);
            Assert.AreEqual("PACKING_INCOMPLETO", r.ConflictoConCodigo.Codigo);
            Assert.AreEqual(1, ((DetallesPackingIncompletoDTO)r.ConflictoConCodigo.Detalles).Faltan);
            A.CallTo(() => pasos.CrearDesdePedido(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.Imprimir(A<int>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, apuntados.Count);
        }

        [TestMethod]
        public async Task Incompleto_Confirmado_ImprimeYApuntaConfirmado()
        {
            EntregaCon(UnoSinLeer());

            ResultadoEtiquetaPacking r = await servicio.Imprimir(PEDIDO, new PeticionEtiquetaPackingDTO
            { Empresa = "1", Peso = 2m, AvisarPackingIncompleto = true, ConfirmarPackingIncompleto = true }, usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, r.Tipo);
            Assert.IsTrue(r.Etiqueta.Impresa);
            Tuple<string, string, int?, object> evento = apuntados.Single();
            Assert.AreEqual(TiposEventoCalidad.ETIQUETA_CON_PACKING_INCOMPLETO, evento.Item1);
            Assert.AreEqual(PEDIDO, evento.Item3);
            string detalle = JsonConvert.SerializeObject(evento.Item4);
            StringAssert.Contains(detalle, "\"Modo\":\"Confirmado\"");
            StringAssert.Contains(detalle, "\"Picking\":99633");
            StringAssert.Contains(detalle, "41262");
        }

        [TestMethod]
        public async Task Incompleto_VersionViejaSinAviso_ImprimeComoSiempreYApuntaSinAviso()
        {
            EntregaCon(UnoSinLeer());

            ResultadoEtiquetaPacking r = await servicio.Imprimir(PEDIDO, new PeticionEtiquetaPackingDTO { Empresa = "1", Peso = 2m }, usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, r.Tipo);
            StringAssert.Contains(JsonConvert.SerializeObject(apuntados.Single().Item4), "\"Modo\":\"SinAviso\"");
        }

        [TestMethod]
        public async Task Completo_ConAviso_ImprimeYNoApunta()
        {
            EntregaCon(new List<ProductoSinLeerPackingDTO>());

            ResultadoEtiquetaPacking r = await servicio.Imprimir(PEDIDO, new PeticionEtiquetaPackingDTO { Empresa = "1", Peso = 2m, AvisarPackingIncompleto = true }, usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, r.Tipo);
            Assert.AreEqual(0, apuntados.Count);
        }

        [TestMethod]
        public async Task SiApuntarFalla_LaEtiquetaSaleIgual()
        {
            EntregaCon(UnoSinLeer());
            servicio.ApuntarCalidad = (a, b, c, d, e, f, g, h, i, j) => throw new InvalidOperationException("caída");

            ResultadoEtiquetaPacking r = await servicio.Imprimir(PEDIDO, new PeticionEtiquetaPackingDTO { Empresa = "1", Peso = 2m, ConfirmarPackingIncompleto = true }, usuario);

            Assert.AreEqual(TipoResultadoEtiquetaPacking.Hecha, r.Tipo);
            Assert.IsTrue(r.Etiqueta.Impresa);
        }

        [TestMethod]
        public void EntregaDelPacking_TraeLoSinLeerYElPicking()
        {
            var packing = new PackingAlmacenDTO
            {
                Picking = 99633,
                Entregas = new List<EntregaPackingAlmacenDTO> { Entrega(Pedido(PEDIDO, Linea("41262", 2, 1))) }
            };

            EntregaParaEtiqueta entrega = EntregaParaEtiqueta.De(packing, PEDIDO);

            Assert.AreEqual(99633, entrega.Picking);
            Assert.AreEqual(1, entrega.SinLeer.Single().Cantidad);
            Assert.IsNull(EntregaParaEtiqueta.De(new List<BultoAlmacenDTO>()).SinLeer, "Sin packing en curso no se sabe: no se avisa");
        }
    }
}
