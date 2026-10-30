using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Rectificativas;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#760 (corte 4 de #755): leer la etiqueta NR{Pedido}-{N} crea el pedido de abono (serie RV, sin líneas) del cliente
    /// de la nota. Decisiones de Carlos del 30/10/26: abono en RV, factura original opcional (con ella, vinculada; sin ella, LIFO
    /// #38), lo factura cualquiera de Almacén; permisos Almacén, Administración, Dirección e Informática.
    /// </summary>
    [TestClass]
    public class AbonosRecogidaTests
    {
        private const string EMPRESA = "1";
        private const int NOTA = 929554;
        private const string CLIENTE = "15191";
        private static readonly DateTime HOY = new DateTime(2026, 10, 30);

        private RepositorioFalso repositorio;
        private List<Exception> registrados;
        private ServicioAbonosRecogida servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioFalso();
            repositorio.Notas[NOTA] = new NotaRecogidaLeida
            {
                Empresa = EMPRESA, Pedido = NOTA, NotaEntrega = true, Cliente = CLIENTE, Contacto = "0", Nombre = "PELUQUERÍA PRUEBA",
                Serie = "NV", Tipo = "Recogida", Bultos = 2, TextoNota = "Recoger secador averiado"
            };
            repositorio.Notas[929555] = new NotaRecogidaLeida
            {
                Empresa = EMPRESA, Pedido = 929555, NotaEntrega = true, Cliente = CLIENTE, Contacto = "0", Serie = "NV", Tipo = "Entrega"
            };
            repositorio.Notas[929556] = new NotaRecogidaLeida
            {
                Empresa = EMPRESA, Pedido = 929556, NotaEntrega = false, Cliente = CLIENTE, Contacto = "0", Serie = "NV", Tipo = "Recogida"
            };
            repositorio.Parada = new ParadaRecogidaLeida
            {
                EnvioId = 251400, Estado = Constantes.Agencias.ESTADO_ENTREGADO, FechaRecogida = HOY.AddHours(11), Receptor = "María"
            };
            repositorio.Ficha = new FichaClienteAbono
            {
                FormaPago = "RCB", Iva = "G21", Vendedor = "ASH", Ruta = "AT", Ccc = "1", ContactoCobro = "0"
            };
            registrados = new List<Exception>();
            servicio = new ServicioAbonosRecogida(repositorio, () => HOY, registrados.Add);
        }

        // ================================================================ GET: resolución del código

        [TestMethod]
        public async Task Leer_EtiquetaDelBulto_DevuelveLaNotaConSuParada()
        {
            ResultadoAbonoRecogida<RecogidaDTO> resultado = await servicio.Leer("NR929554-1", true);

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            RecogidaDTO recogida = resultado.Contenido;
            Assert.AreEqual(EMPRESA, recogida.Empresa);
            Assert.AreEqual(NOTA, recogida.PedidoNota);
            Assert.AreEqual(CLIENTE, recogida.Cliente);
            Assert.AreEqual("0", recogida.Contacto);
            Assert.AreEqual("PELUQUERÍA PRUEBA", recogida.Nombre);
            Assert.AreEqual("Recoger secador averiado", recogida.TextoNota);
            Assert.AreEqual(2, recogida.Bultos);
            Assert.AreEqual("Entregado", recogida.EstadoEnvio);
            Assert.AreEqual(HOY.AddHours(11), recogida.FechaRecogida);
            Assert.AreEqual("María", recogida.Receptor);
            Assert.IsNull(recogida.PedidoAbono);
        }

        [TestMethod]
        public async Task Leer_CodigoDelDocumentoEnMinusculasYConEspacios_TambienVale()
        {
            Assert.AreEqual(NOTA, (await servicio.Leer(" nr929554 ", true)).Contenido.PedidoNota);
        }

        [TestMethod]
        public async Task Leer_CodigosQueNoSonDeRecogida_404()
        {
            foreach (string codigo in new[] { "NE929554", "929554", "NR929554-0", "", null, "RP251400-1" })
            {
                ResultadoAbonoRecogida<RecogidaDTO> resultado = await servicio.Leer(codigo, true);
                Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, resultado.Tipo, codigo ?? "(null)");
            }
            StringAssert.Contains((await servicio.Leer("NE929554", true)).Mensaje, "no es una etiqueta de recogida");
        }

        [TestMethod]
        public async Task Leer_NotaDeEntregaPedidoNormalOInexistente_404()
        {
            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, (await servicio.Leer("NR929555", true)).Tipo, "es de entrega");
            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, (await servicio.Leer("NR929556", true)).Tipo, "no es nota");
            ResultadoAbonoRecogida<RecogidaDTO> inexistente = await servicio.Leer("NR1", true);
            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, inexistente.Tipo);
            StringAssert.Contains(inexistente.Mensaje, "no es una nota de recogida");
        }

        [TestMethod]
        public async Task Leer_SinDatosPersonales_NoEnseñaElReceptor()
        {
            RecogidaDTO recogida = (await servicio.Leer("NR929554-1", false)).Contenido;

            Assert.IsNull(recogida.Receptor);
            Assert.AreEqual(HOY.AddHours(11), recogida.FechaRecogida, "la fecha sí");
        }

        [TestMethod]
        public async Task Leer_SinEnvio_EstadoNull()
        {
            repositorio.Parada = null;

            RecogidaDTO recogida = (await servicio.Leer("NR929554", true)).Contenido;

            Assert.IsNull(recogida.EstadoEnvio);
            Assert.IsNull(recogida.FechaRecogida);
        }

        [TestMethod]
        public async Task Leer_ConAbonoQueSigueExistiendo_LoDevuelve_YSiLoHanBorrado_Null()
        {
            repositorio.Notas[NOTA].PedidoAbono = 930700;
            repositorio.Pedidos[930700] = new PedidoAbonoLeido { Cliente = CLIENTE, Contacto = "0", Serie = "RV" };
            Assert.AreEqual(930700, (await servicio.Leer("NR929554", true)).Contenido.PedidoAbono);

            repositorio.Pedidos.Remove(930700);
            Assert.IsNull((await servicio.Leer("NR929554", true)).Contenido.PedidoAbono);
        }

        // ================================================================ POST: creación del abono

        [TestMethod]
        public async Task CrearAbono_Recogida_CreaLaCabeceraEnRVConLosDatosDelCliente()
        {
            repositorio.SiguienteNumero = 930800;

            ResultadoAbonoRecogida<AbonoRecogidaDTO> resultado = await servicio.CrearAbono(Peticion("NR929554-2"), "NUEVAVISION\\alfredo");

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            Assert.IsTrue(resultado.Creado);
            AbonoRecogidaDTO abono = resultado.Contenido;
            Assert.AreEqual(EMPRESA, abono.Empresa);
            Assert.AreEqual(930800, abono.Pedido);
            Assert.AreEqual(CLIENTE, abono.Cliente);
            Assert.AreEqual("0", abono.Contacto);
            Assert.AreEqual("PELUQUERÍA PRUEBA", abono.Nombre);
            Assert.AreEqual("RV", abono.Serie);
            Assert.AreEqual(NOTA, abono.PedidoNota);
            Assert.IsNull(abono.FacturaOriginal);
            Assert.IsFalse(abono.YaExistia);

            Guardado guardado = repositorio.Guardados.Single();
            CabPedidoVta cabecera = guardado.Cabecera;
            Assert.AreEqual(EMPRESA, cabecera.Empresa);
            Assert.AreEqual(930800, cabecera.Número);
            Assert.AreEqual(CLIENTE, cabecera.Nº_Cliente);
            Assert.AreEqual("0", cabecera.Contacto);
            Assert.AreEqual(HOY, cabecera.Fecha);
            Assert.AreEqual("RV", cabecera.Serie);
            Assert.AreEqual(Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, cabecera.Periodo_Facturacion);
            Assert.AreEqual("RCB", cabecera.Forma_Pago, "la forma de pago de la ficha");
            Assert.AreEqual(Constantes.PlazosPago.CONTADO, cabecera.PlazosPago, "rectificativa al contado, como Copiar factura");
            Assert.AreEqual(HOY, cabecera.Primer_Vencimiento);
            Assert.AreEqual("G21", cabecera.IVA);
            Assert.AreEqual("ASH", cabecera.Vendedor);
            Assert.AreEqual("AT", cabecera.Ruta);
            Assert.AreEqual("1", cabecera.CCC);
            Assert.AreEqual("0", cabecera.ContactoCobro);
            Assert.AreEqual(EMPRESA, cabecera.Origen);
            Assert.IsFalse(cabecera.NotaEntrega);
            Assert.IsFalse(cabecera.MantenerJunto, "al contado no hay «mantener junto» (#686)");
            Assert.IsNull(cabecera.TipoRectificativa, "sin factura original: LIFO, tipo por defecto de la serie");
            Assert.AreEqual("Abono por recogida: nota 929554 — Recoger secador averiado", cabecera.Comentarios);
            Assert.AreEqual("NUEVAVISION\\alfredo", cabecera.Usuario);
            Assert.AreEqual(0, cabecera.LinPedidoVtas.Count, "sin líneas: las añade Nesto por el PUT");

            Assert.AreEqual(NOTA, guardado.PedidoNota);
            Assert.IsNull(guardado.Anterior);
            Assert.AreEqual("NUEVAVISION\\alfredo", guardado.Usuario);
            Assert.AreEqual("Pedidos", guardado.Modificacion.Tabla);
            StringAssert.Contains(guardado.Modificacion.Nuevo, "Pedido de abono 930800");
            StringAssert.Contains(guardado.Modificacion.Nuevo, "nota de recogida 929554");
            Assert.AreEqual("NUEVAVISION\\alfredo", guardado.Modificacion.Usuario);
        }

        [TestMethod]
        public async Task CrearAbono_ConFacturaOriginal_LaDevuelveYMarcaR1()
        {
            repositorio.Notas[NOTA].FacturaOriginal = "NV26/001234";

            ResultadoAbonoRecogida<AbonoRecogidaDTO> resultado = await servicio.CrearAbono(Peticion("NR929554"), "alfredo");

            Assert.AreEqual("NV26/001234", resultado.Contenido.FacturaOriginal);
            CabPedidoVta cabecera = repositorio.Guardados.Single().Cabecera;
            Assert.AreEqual("R1", cabecera.TipoRectificativa);
            StringAssert.Contains(repositorio.Guardados.Single().Modificacion.Nuevo, "factura original NV26/001234");
        }

        [TestMethod]
        public async Task CrearAbono_YaTieneAbono_200YaExistiaSinCrearOtro()
        {
            repositorio.Notas[NOTA].PedidoAbono = 930700;
            repositorio.Pedidos[930700] = new PedidoAbonoLeido { Cliente = CLIENTE, Contacto = "0", Serie = "RV" };
            repositorio.Parada.Estado = Constantes.Agencias.ESTADO_TRAMITADO; // ni siquiera pregunta: ya está creado

            ResultadoAbonoRecogida<AbonoRecogidaDTO> resultado = await servicio.CrearAbono(Peticion("NR929554-2"), "santiago");

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            Assert.IsFalse(resultado.Creado);
            Assert.IsTrue(resultado.Contenido.YaExistia);
            Assert.AreEqual(930700, resultado.Contenido.Pedido);
            Assert.AreEqual("RV", resultado.Contenido.Serie);
            Assert.AreEqual(0, repositorio.Guardados.Count);
            Assert.AreEqual(0, repositorio.NumerosReservados);
        }

        [TestMethod]
        public async Task CrearAbono_DosLecturasSeguidas_LaSegundaAbreElMismo()
        {
            repositorio.SiguienteNumero = 930801;
            AbonoRecogidaDTO primero = (await servicio.CrearAbono(Peticion("NR929554-1"), "alfredo")).Contenido;

            ResultadoAbonoRecogida<AbonoRecogidaDTO> segundo = await servicio.CrearAbono(Peticion("NR929554-2"), "alfredo");

            Assert.IsTrue(segundo.Contenido.YaExistia);
            Assert.AreEqual(primero.Pedido, segundo.Contenido.Pedido);
            Assert.AreEqual(1, repositorio.Guardados.Count);
        }

        [TestMethod]
        public async Task CrearAbono_ElAbonoAnteriorLoHanBorrado_CreaOtroDesdeElAnterior()
        {
            repositorio.Notas[NOTA].PedidoAbono = 930700; // ya no existe en Pedidos
            repositorio.SiguienteNumero = 930802;

            ResultadoAbonoRecogida<AbonoRecogidaDTO> resultado = await servicio.CrearAbono(Peticion("NR929554"), "alfredo");

            Assert.IsTrue(resultado.Creado);
            Assert.AreEqual(930802, resultado.Contenido.Pedido);
            Assert.AreEqual(930700, repositorio.Guardados.Single().Anterior, "el UPDATE solo entra si sigue apuntando al borrado");
        }

        [TestMethod]
        public async Task CrearAbono_OtroSeAdelanta_DevuelveElDelOtro()
        {
            repositorio.SiguienteNumero = 930803;
            repositorio.AlGuardarOtroGana = 930750;

            ResultadoAbonoRecogida<AbonoRecogidaDTO> resultado = await servicio.CrearAbono(Peticion("NR929554"), "alfredo");

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            Assert.IsFalse(resultado.Creado);
            Assert.IsTrue(resultado.Contenido.YaExistia);
            Assert.AreEqual(930750, resultado.Contenido.Pedido);
        }

        [TestMethod]
        public async Task CrearAbono_SinRecoger_409ConEstado_YConForzarLoCrea()
        {
            repositorio.Parada.Estado = Constantes.Agencias.ESTADO_TRAMITADO;

            ResultadoAbonoRecogida<AbonoRecogidaDTO> sinForzar = await servicio.CrearAbono(Peticion("NR929554-1"), "alfredo");

            Assert.AreEqual(TipoResultadoNotaEntrega.Conflicto, sinForzar.Tipo);
            Assert.AreEqual(ServicioAbonosRecogida.CODIGO_NO_RECOGIDA, sinForzar.Codigo);
            Assert.AreEqual("La recogida de la nota 929554 no figura como recogida (estado Tramitado).", sinForzar.Mensaje);
            Assert.AreEqual("Tramitado", Propiedad(sinForzar.Detalles, "estado"));
            Assert.AreEqual(251400, Propiedad(sinForzar.Detalles, "envio"));
            Assert.AreEqual(0, repositorio.Guardados.Count);

            ResultadoAbonoRecogida<AbonoRecogidaDTO> forzado = await servicio.CrearAbono(Peticion("NR929554-1", forzar: true), "alfredo");

            Assert.IsTrue(forzado.Creado);
            Assert.AreEqual(1, repositorio.Guardados.Count);
        }

        [TestMethod]
        public async Task CrearAbono_OtroGanaAlGuardar_DevuelveElNumeroReservado()
        {
            // Revisión 30/10/26: el número de pedido se reserva antes de guardar; si el abono no se graba (carrera o error) se
            // devuelve al contador para no quemarlo.
            repositorio.AlGuardarOtroGana = 777;
            int esperado = repositorio.SiguienteNumero;

            _ = await servicio.CrearAbono(Peticion("NR929554-2"), "alfredo");

            CollectionAssert.AreEqual(new[] { esperado }, repositorio.NumerosDevueltos);
        }

        [TestMethod]
        public async Task CrearAbono_SinEnvio_409SinEnvio()
        {
            repositorio.Parada = null;

            ResultadoAbonoRecogida<AbonoRecogidaDTO> resultado = await servicio.CrearAbono(Peticion("NR929554"), "alfredo");

            Assert.AreEqual(TipoResultadoNotaEntrega.Conflicto, resultado.Tipo);
            Assert.AreEqual(ServicioAbonosRecogida.ESTADO_SIN_ENVIO, Propiedad(resultado.Detalles, "estado"));
            StringAssert.Contains(resultado.Mensaje, "no tiene envío de reparto propio");
        }

        [TestMethod]
        public async Task CrearAbono_SinCodigoOSinFicha_400_YCodigoMalo_404()
        {
            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, (await servicio.CrearAbono(null, "alfredo")).Tipo);
            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, (await servicio.CrearAbono(Peticion(" "), "alfredo")).Tipo);
            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, (await servicio.CrearAbono(Peticion("NE929554"), "alfredo")).Tipo);
            repositorio.Ficha = null;
            ResultadoAbonoRecogida<AbonoRecogidaDTO> sinFicha = await servicio.CrearAbono(Peticion("NR929554"), "alfredo");
            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, sinFicha.Tipo);
            StringAssert.Contains(sinFicha.Mensaje, "No se encuentra la ficha del cliente 15191/0");
            Assert.AreEqual(0, repositorio.Guardados.Count);
        }

        [TestMethod]
        public void ConstruirCabecera_SinFormaPagoEnLaFicha_EfectivoYUsuarioRecortadoA30()
        {
            var ficha = new FichaClienteAbono { Iva = "G21", Ruta = "16" };
            string usuario = new string('x', 45);

            CabPedidoVta cabecera = ServicioAbonosRecogida.ConstruirCabecera(repositorio.Notas[NOTA], ficha, 1, HOY, usuario);

            Assert.AreEqual(Constantes.FormasPago.EFECTIVO, cabecera.Forma_Pago);
            Assert.IsNull(cabecera.CCC);
            Assert.AreEqual(30, cabecera.Usuario.Length);
        }

        [TestMethod]
        public void Comentario_SinTexto_SoloLaNota()
        {
            Assert.AreEqual("Abono por recogida: nota 929554",
                ServicioAbonosRecogida.Comentario(new NotaRecogidaLeida { Pedido = NOTA, TextoNota = "  " }));
        }

        [TestMethod]
        public void SerieAbono_LaRectificativaDeLaSerieDeLaNota()
        {
            Assert.AreEqual("RV", ServicioAbonosRecogida.SerieAbono("NV"));
            Assert.AreEqual("RV", ServicioAbonosRecogida.SerieAbono("NV "));
            Assert.AreEqual("RC", ServicioAbonosRecogida.SerieAbono("CV"));
            Assert.AreEqual("RV", ServicioAbonosRecogida.SerieAbono("RV"), "ya es rectificativa");
            Assert.AreEqual("RV", ServicioAbonosRecogida.SerieAbono(null), "sin serie, la de NV");
            Assert.AreEqual("RV", ServicioAbonosRecogida.SerieAbono("XX"), "serie sin rectificativa asociada: la de NV");
        }

        // ================================================================ PUT del pedido: aviso de producto fuera de la factura

        [TestMethod]
        public async Task Aviso_ProductoQueNoEstaEnLaFactura_AvisoConElProducto()
        {
            repositorio.FacturaDelAbono = "NV26/001234";
            repositorio.ProductosFactura = new List<string> { "12345" };

            AvisoPedidoDTO aviso = await servicio.AvisoProductosFueraDeFactura(Abono(("12345", -1), ("99999 ", -2)));

            Assert.IsNotNull(aviso);
            Assert.AreEqual(ServicioAbonosRecogida.TIPO_AVISO_FUERA_DE_FACTURA, aviso.Tipo);
            Assert.AreEqual("El producto 99999 no está en la factura NV26/001234.", aviso.Mensaje);
        }

        [TestMethod]
        public async Task Aviso_VariosFuera_EnPlural()
        {
            repositorio.FacturaDelAbono = "NV26/001234";

            AvisoPedidoDTO aviso = await servicio.AvisoProductosFueraDeFactura(Abono(("11111", -1), ("22222", -1)));

            Assert.AreEqual("Los productos 11111, 22222 no están en la factura NV26/001234.", aviso.Mensaje);
        }

        [TestMethod]
        public async Task Aviso_TodosEnLaFacturaSinFacturaOriginalOSerieNormal_Null()
        {
            repositorio.FacturaDelAbono = "NV26/001234";
            repositorio.ProductosFactura = new List<string> { "12345" };
            Assert.IsNull(await servicio.AvisoProductosFueraDeFactura(Abono(("12345", -1))), "todo en la factura");

            PedidoVentaDTO normal = Abono(("99999", -1));
            normal.serie = "NV";
            Assert.IsNull(await servicio.AvisoProductosFueraDeFactura(normal), "serie normal: ni se mira");

            Assert.IsNull(await servicio.AvisoProductosFueraDeFactura(Abono(("99999", 1))), "solo cuentan las líneas en negativo");

            repositorio.FacturaDelAbono = null;
            Assert.IsNull(await servicio.AvisoProductosFueraDeFactura(Abono(("99999", -1))), "sin factura original");
            Assert.IsNull(await servicio.AvisoProductosFueraDeFactura(null));
        }

        [TestMethod]
        public async Task Aviso_SiFallaLaLectura_NullYAElmah()
        {
            repositorio.FallarFactura = true;

            Assert.IsNull(await servicio.AvisoProductosFueraDeFactura(Abono(("99999", -1))));
            Assert.AreEqual(1, registrados.Count);
        }

        // ================================================================ facturación: la factura original va primero

        [TestMethod]
        public void PreferidaPrimero_LaFacturaDeLaNotaDelanteYElRestoEnLifo()
        {
            var compras = new List<GestorFacturasRectificativas.CompraOriginal>
            {
                new GestorFacturasRectificativas.CompraOriginal { Factura = "NV26/003000", Linea = 1, Cantidad = 5 },
                new GestorFacturasRectificativas.CompraOriginal { Factura = "NV26/002000", Linea = 2, Cantidad = 5 },
                new GestorFacturasRectificativas.CompraOriginal { Factura = "NV26/001234", Linea = 3, Cantidad = 1 }
            };

            List<GestorFacturasRectificativas.CompraOriginal> ordenadas = GestorFacturasRectificativas.PreferidaPrimero(compras, "nv26/001234 ");
            List<NestoAPI.Models.Rectificativas.VinculacionRectificativa> vinculaciones =
                GestorFacturasRectificativas.RepartirEntreCompras("12345", 3, ordenadas);

            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, ordenadas.Select(c => c.Linea).ToArray());
            Assert.AreEqual("NV26/001234", vinculaciones[0].FacturaOriginalNumero, "primero la factura que dice la nota");
            Assert.AreEqual(1m, vinculaciones[0].CantidadRectificada);
            Assert.AreEqual("NV26/003000", vinculaciones[1].FacturaOriginalNumero, "lo que no cabe, por el LIFO");
            Assert.AreEqual(2m, vinculaciones[1].CantidadRectificada);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, GestorFacturasRectificativas.PreferidaPrimero(compras, null).Select(c => c.Linea).ToArray());
        }

        // ================================================================ controlador

        [TestMethod]
        public async Task Controlador_FueraDeLosGrupos_403EnElGetYEnElPost()
        {
            NotasEntregaController controlador = Controlador(Usuario("vendedor", "Ventas"));

            Assert.AreEqual(HttpStatusCode.Forbidden, (await Ejecutar(await controlador.GetRecogida("NR929554"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Ejecutar(await controlador.PostAbonoRecogida(Peticion("NR929554")))).StatusCode);
            Assert.AreEqual(0, repositorio.Guardados.Count);
        }

        [TestMethod]
        public async Task Controlador_LosCuatroGrupos_Pueden()
        {
            foreach (string grupo in new[] { Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.ADMINISTRACION,
                Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA })
            {
                Assert.AreEqual(HttpStatusCode.OK, (await Ejecutar(await Controlador(Usuario("x", grupo)).GetRecogida("NR929554"))).StatusCode, grupo);
            }
        }

        [TestMethod]
        public async Task Controlador_Get_InformaticaNoVeElReceptor_AlmacenSi()
        {
            var informatica = (await Controlador(Usuario("carlos", Constantes.GruposSeguridad.INFORMATICA)).GetRecogida("NR929554-1"))
                as OkNegotiatedContentResult<RecogidaDTO>;
            var almacen = (await Controlador(Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)).GetRecogida("NR929554-1"))
                as OkNegotiatedContentResult<RecogidaDTO>;

            Assert.IsNull(informatica.Content.Receptor);
            Assert.AreEqual("María", almacen.Content.Receptor);
        }

        [TestMethod]
        public async Task Controlador_Get_NoEsRecogida_404()
        {
            Assert.AreEqual(HttpStatusCode.NotFound,
                (await Ejecutar(await Controlador(Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)).GetRecogida("NE929554"))).StatusCode);
        }

        [TestMethod]
        public async Task Controlador_Post_201AlCrear_200SiYaExistia_ConElUsuarioDelToken()
        {
            NotasEntregaController controlador = Controlador(Usuario("Alfredo", Constantes.GruposSeguridad.ALMACEN));

            HttpResponseMessage creado = await Ejecutar(await controlador.PostAbonoRecogida(Peticion("NR929554-1")));
            HttpResponseMessage otra = await Ejecutar(await Controlador(Usuario("Alfredo", Constantes.GruposSeguridad.ALMACEN))
                .PostAbonoRecogida(Peticion("NR929554-2")));

            Assert.AreEqual(HttpStatusCode.Created, creado.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, otra.StatusCode);
            StringAssert.Contains(await otra.Content.ReadAsStringAsync(), "\"YaExistia\":true");
            Assert.AreEqual("Alfredo", repositorio.Guardados.Single().Cabecera.Usuario);
        }

        [TestMethod]
        public async Task Controlador_Post_SinRecoger_409ConCodigoYDetalles()
        {
            repositorio.Parada.Estado = Constantes.Agencias.ESTADO_INCIDENTADO;

            HttpResponseMessage respuesta = await Ejecutar(await Controlador(Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN))
                .PostAbonoRecogida(Peticion("NR929554-1")));

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            string cuerpo = await respuesta.Content.ReadAsStringAsync();
            StringAssert.Contains(cuerpo, "\"code\":\"RECOGIDA_NO_RECOGIDA\"");
            StringAssert.Contains(cuerpo, "\"estado\":\"Incidentado\"");
            StringAssert.Contains(cuerpo, "\"Message\":");
        }

        // ================================================================ ayudantes

        private NotasEntregaController Controlador(IPrincipal usuario) => new NotasEntregaController(null, null)
        {
            Request = new HttpRequestMessage(),
            Configuration = new HttpConfiguration(),
            User = usuario,
            AbonosRecogida = servicio
        };

        private static Task<HttpResponseMessage> Ejecutar(IHttpActionResult resultado) => resultado.ExecuteAsync(CancellationToken.None);

        private static ClaimsPrincipal Usuario(string nombre, string grupo) => new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, nombre), new Claim(ClaimTypes.Role, grupo)
        }, "prueba"));

        private static CrearAbonoRecogidaDTO Peticion(string codigo, bool forzar = false) => new CrearAbonoRecogidaDTO { Codigo = codigo, Forzar = forzar };

        private static object Propiedad(object objeto, string nombre) => objeto.GetType().GetProperty(nombre).GetValue(objeto);

        private static PedidoVentaDTO Abono(params (string producto, short cantidad)[] lineas) => new PedidoVentaDTO
        {
            empresa = EMPRESA,
            numero = 930800,
            serie = "RV",
            Lineas = lineas.Select(l => new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = l.producto, Cantidad = l.cantidad
            }).ToList()
        };

        private class Guardado
        {
            public CabPedidoVta Cabecera;
            public Modificacion Modificacion;
            public int PedidoNota;
            public int? Anterior;
            public string Usuario;
        }

        private class RepositorioFalso : IRepositorioAbonosRecogida
        {
            public bool Script = true;
            public readonly Dictionary<int, NotaRecogidaLeida> Notas = new Dictionary<int, NotaRecogidaLeida>();
            public readonly Dictionary<int, PedidoAbonoLeido> Pedidos = new Dictionary<int, PedidoAbonoLeido>();
            public readonly List<Guardado> Guardados = new List<Guardado>();
            public ParadaRecogidaLeida Parada;
            public FichaClienteAbono Ficha;
            public int SiguienteNumero = 930800;
            public int NumerosReservados;
            public int? AlGuardarOtroGana;
            public string FacturaDelAbono;
            public bool FallarFactura;
            public List<string> ProductosFactura = new List<string>();

            public bool ScriptAplicado() => Script;

            public Task<NotaRecogidaLeida> LeerNota(string empresa, int pedido)
                => Task.FromResult(Notas.TryGetValue(pedido, out NotaRecogidaLeida nota) ? Copia(nota) : null);

            public Task<ParadaRecogidaLeida> LeerParada(string empresa, int pedido) => Task.FromResult(Parada);

            public Task<PedidoAbonoLeido> LeerPedido(string empresa, int numero)
                => Task.FromResult(Pedidos.TryGetValue(numero, out PedidoAbonoLeido p) ? p : null);

            public Task<FichaClienteAbono> LeerFicha(string empresa, string cliente, string contacto) => Task.FromResult(Ficha);

            public int ReservarNumeroPedido()
            {
                NumerosReservados++;
                return SiguienteNumero++;
            }

            public List<int> NumerosDevueltos { get; } = new List<int>();
            public void DevolverNumeroPedido(int numero) => NumerosDevueltos.Add(numero);

            public Task<bool> GuardarAbono(CabPedidoVta cabecera, Modificacion modificacion, int pedidoNota, int? pedidoAbonoAnterior, string usuario)
            {
                if (AlGuardarOtroGana.HasValue)
                {
                    Notas[pedidoNota].PedidoAbono = AlGuardarOtroGana;
                    Pedidos[AlGuardarOtroGana.Value] = new PedidoAbonoLeido { Cliente = cabecera.Nº_Cliente, Contacto = cabecera.Contacto, Serie = "RV" };
                    return Task.FromResult(false);
                }
                if (Notas[pedidoNota].PedidoAbono != pedidoAbonoAnterior)
                {
                    return Task.FromResult(false);
                }
                Guardados.Add(new Guardado { Cabecera = cabecera, Modificacion = modificacion, PedidoNota = pedidoNota, Anterior = pedidoAbonoAnterior, Usuario = usuario });
                Notas[pedidoNota].PedidoAbono = cabecera.Número;
                Pedidos[cabecera.Número] = new PedidoAbonoLeido { Cliente = cabecera.Nº_Cliente, Contacto = cabecera.Contacto, Serie = cabecera.Serie };
                return Task.FromResult(true);
            }

            public Task<List<string>> LeerProductosFactura(string empresa, string factura) => Task.FromResult(ProductosFactura);

            public Task<string> LeerFacturaOriginalDelAbono(string empresa, int pedidoAbono)
            {
                if (FallarFactura)
                {
                    throw new InvalidOperationException("se denegó el permiso");
                }
                return Task.FromResult(FacturaDelAbono);
            }

            private static NotaRecogidaLeida Copia(NotaRecogidaLeida n) => new NotaRecogidaLeida
            {
                Empresa = n.Empresa, Pedido = n.Pedido, NotaEntrega = n.NotaEntrega, Cliente = n.Cliente, Contacto = n.Contacto, Nombre = n.Nombre,
                Serie = n.Serie, Tipo = n.Tipo, Bultos = n.Bultos, FacturaOriginal = n.FacturaOriginal, PedidoAbono = n.PedidoAbono, TextoNota = n.TextoNota
            };
        }
    }
}
