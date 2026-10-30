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
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#757 (corte 1 de #755): tipo (Entrega / Recogida), bultos y factura original de las notas de entrega.
    /// Decisiones de Carlos del 30/10/26: las de Entrega pasan por packing (los bultos salen de ahí), así que Bultos solo se
    /// valida en Recogida; FacturaOriginal en una Entrega se ignora.
    /// </summary>
    [TestClass]
    public class NotasEntregaCabeceraTests
    {
        private const string EMPRESA = "1";
        private const int NOTA = 930500;
        private const string CLIENTE = "15191";

        private RepositorioFalso repositorio;
        private List<Exception> registrados;
        private ServicioNotasEntregaCabecera servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioFalso();
            repositorio.Pedidos[NOTA] = new PedidoNotaEntrega { Cliente = CLIENTE, NotaEntrega = true };
            repositorio.Pedidos[930501] = new PedidoNotaEntrega { Cliente = CLIENTE, NotaEntrega = false };
            repositorio.Facturas["NV2600123"] = CLIENTE;
            repositorio.Facturas["NV2600999"] = "9999";
            registrados = new List<Exception>();
            servicio = new ServicioNotasEntregaCabecera(repositorio, registrados.Add);
        }

        // ================================================================ GET: cabecera por defecto y campos

        [TestMethod]
        public async Task Leer_NotaSinFila_EntregaUnBultoYCodigoNE()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Leer(EMPRESA, NOTA);

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            NotaEntregaCabeceraDTO nota = resultado.Contenido;
            Assert.AreEqual(EMPRESA, nota.Empresa);
            Assert.AreEqual(NOTA, nota.Pedido);
            Assert.AreEqual("Entrega", nota.Tipo);
            Assert.AreEqual(1, nota.Bultos);
            Assert.IsNull(nota.FacturaOriginal);
            Assert.IsNull(nota.PedidoAbono);
            Assert.IsNull(nota.NumeroNota);
            Assert.IsFalse(nota.Procesada);
            Assert.IsNull(nota.EnvioId);
            Assert.IsNull(nota.EstadoEnvio);
            Assert.AreEqual("NE930500", nota.CodigoDocumento);
        }

        [TestMethod]
        public async Task Leer_RecogidaProcesadaConEnvio_TraeTodoYCodigoNR()
        {
            repositorio.Filas[NOTA] = new FilaNotaEntregaCabecera
            {
                Empresa = EMPRESA, Pedido = NOTA, Tipo = "Recogida", Bultos = 3, FacturaOriginal = "NV2600123", PedidoAbono = 930600
            };
            repositorio.NumeroNota = 45123;
            repositorio.Procesada = true;
            repositorio.Envio = new EnvioNotaEntrega { Numero = 251400, Estado = Constantes.Agencias.ESTADO_INCIDENTADO };

            NotaEntregaCabeceraDTO nota = (await servicio.Leer(EMPRESA, NOTA)).Contenido;

            Assert.AreEqual("Recogida", nota.Tipo);
            Assert.AreEqual(3, nota.Bultos);
            Assert.AreEqual("NV2600123", nota.FacturaOriginal);
            Assert.AreEqual(930600, nota.PedidoAbono);
            Assert.AreEqual(45123, nota.NumeroNota);
            Assert.IsTrue(nota.Procesada);
            Assert.AreEqual(251400, nota.EnvioId);
            Assert.AreEqual("Incidentado", nota.EstadoEnvio);
            Assert.AreEqual("NR930500", nota.CodigoDocumento);
        }

        [TestMethod]
        public void EstadoEnvio_SoloTramitadoIncidentadoYEntregado()
        {
            Assert.AreEqual("Tramitado", ServicioNotasEntregaCabecera.EstadoEnvio(Constantes.Agencias.ESTADO_TRAMITADO));
            Assert.AreEqual("Entregado", ServicioNotasEntregaCabecera.EstadoEnvio(Constantes.Agencias.ESTADO_ENTREGADO));
            Assert.AreEqual("Incidentado", ServicioNotasEntregaCabecera.EstadoEnvio(Constantes.Agencias.ESTADO_INCIDENTADO));
            Assert.IsNull(ServicioNotasEntregaCabecera.EstadoEnvio(0));
            Assert.IsNull(ServicioNotasEntregaCabecera.EstadoEnvio(-1));
            Assert.IsNull(ServicioNotasEntregaCabecera.EstadoEnvio(null));
        }

        [TestMethod]
        public async Task Leer_PedidoQueNoEsNota_NoEncontrado()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Leer(EMPRESA, 930501);

            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "no es una nota de entrega");
        }

        [TestMethod]
        public async Task Leer_PedidoQueNoExiste_NoEncontrado()
        {
            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, (await servicio.Leer(EMPRESA, 1)).Tipo);
        }

        // ================================================================ PUT: validaciones

        [TestMethod]
        public async Task Actualizar_PedidoQueNoEsNota_400DeUsted()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, 930501, Peticion("Recogida", 2), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "no es una nota de entrega");
            Assert.AreEqual(0, repositorio.Guardadas.Count);
        }

        [TestMethod]
        public async Task Actualizar_PedidoQueNoExiste_404()
        {
            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, (await servicio.Actualizar(EMPRESA, 1, Peticion("Entrega", 1), "carlos")).Tipo);
        }

        [TestMethod]
        public async Task Actualizar_SinCuerpo_400()
        {
            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, (await servicio.Actualizar(EMPRESA, NOTA, null, "carlos")).Tipo);
        }

        [TestMethod]
        public async Task Actualizar_TipoQueNoExiste_400()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Devolución", 1), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "«Entrega» o «Recogida»");
            StringAssert.Contains(resultado.Mensaje, "Devolución");
        }

        [TestMethod]
        public async Task Actualizar_SinTipo_400()
        {
            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, (await servicio.Actualizar(EMPRESA, NOTA, Peticion(null, 1), "carlos")).Tipo);
        }

        [TestMethod]
        public async Task Actualizar_TipoEnMinusculas_SeNormaliza()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion(" recogida ", 2), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            Assert.AreEqual("Recogida", repositorio.Guardadas.Single().Tipo);
        }

        [TestMethod]
        public async Task Actualizar_RecogidaConCeroBultos_400()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 0), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "al menos un bulto");
            Assert.AreEqual(0, repositorio.Guardadas.Count);
        }

        [TestMethod]
        public async Task Actualizar_RecogidaConMilBultos_400()
        {
            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, (await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 1000), "carlos")).Tipo);
        }

        [TestMethod]
        public async Task Actualizar_RecogidaSinBultos_GuardaUno()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", null), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            Assert.AreEqual(1, repositorio.Guardadas.Single().Bultos);
        }

        [TestMethod]
        public async Task Actualizar_EntregaConCeroBultosYFactura_NoSeValidaYSeGuardaElDefecto()
        {
            // Las de entrega pasan por packing (Carlos 30/10/26): sus bultos salen de ahí y la factura original es de las recogidas.
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Entrega", 0, "NV2600999"), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            FilaNotaEntregaCabecera guardada = repositorio.Guardadas.Single();
            Assert.AreEqual("Entrega", guardada.Tipo);
            Assert.AreEqual(1, guardada.Bultos);
            Assert.IsNull(guardada.FacturaOriginal);
            Assert.AreEqual("NE930500", resultado.Contenido.CodigoDocumento);
        }

        [TestMethod]
        public async Task Actualizar_FacturaQueNoExiste_400()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 1, "NV2699999"), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "La factura NV2699999 no existe");
        }

        [TestMethod]
        public async Task Actualizar_FacturaDeOtroCliente_400()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 1, "NV2600999"), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "es del cliente 9999");
            Assert.AreEqual(0, repositorio.Guardadas.Count);
        }

        [TestMethod]
        public async Task Actualizar_RecogidaConFacturaDelCliente_LaGuardaEnMayusculas()
        {
            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 2, " nv2600123 "), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, resultado.Tipo);
            FilaNotaEntregaCabecera guardada = repositorio.Guardadas.Single();
            Assert.AreEqual("Recogida", guardada.Tipo);
            Assert.AreEqual(2, guardada.Bultos);
            Assert.AreEqual("NV2600123", guardada.FacturaOriginal);
            Assert.AreEqual("carlos", guardada.Usuario);
            Assert.AreEqual("NV2600123", resultado.Contenido.FacturaOriginal);
            Assert.AreEqual("NR930500", resultado.Contenido.CodigoDocumento);
        }

        [TestMethod]
        public async Task Actualizar_EnvioYaEntregado_409()
        {
            repositorio.Envio = new EnvioNotaEntrega { Numero = 251400, Estado = Constantes.Agencias.ESTADO_ENTREGADO };

            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 2), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Conflicto, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "ya está entregada");
            Assert.AreEqual(0, repositorio.Guardadas.Count);
        }

        [TestMethod]
        public async Task Actualizar_EnvioTramitado_SePuede()
        {
            repositorio.Envio = new EnvioNotaEntrega { Numero = 251400, Estado = Constantes.Agencias.ESTADO_TRAMITADO };

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, (await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 2), "carlos")).Tipo);
        }

        [TestMethod]
        public async Task Actualizar_SinScript_400FaltaElScript()
        {
            repositorio.Script = false;

            ResultadoNotaEntregaCabecera resultado = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 2), "carlos");

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "Issue757_NotasEntregaCabecera.sql");
        }

        [TestMethod]
        public async Task Actualizar_DejaFilaEnModificacionesConQuienYQue()
        {
            _ = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 2, "NV2600123"), "NUEVAVISION\\Alfredo");

            Modificacion primera = repositorio.Modificaciones.Single();
            Assert.AreEqual("NotasEntregaCabecera", primera.Tabla);
            StringAssert.Contains(primera.Anterior, "sin fila (Entrega, 1 bulto)");
            StringAssert.Contains(primera.Nuevo, "Tipo=Recogida, Bultos=2, FacturaOriginal=NV2600123");
            StringAssert.Contains(primera.Nuevo, "NUEVAVISION\\Alfredo");
            Assert.AreEqual("NUEVAVISION\\Alfredo", primera.Usuario);

            _ = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Entrega", 5), "NUEVAVISION\\Alfredo");

            Modificacion segunda = repositorio.Modificaciones.Last();
            StringAssert.Contains(segunda.Anterior, "Tipo=Recogida, Bultos=2");
            StringAssert.Contains(segunda.Nuevo, "Tipo=Entrega, Bultos=1, FacturaOriginal=(ninguna)");
        }

        [TestMethod]
        public async Task Actualizar_ConservaElPedidoAbono()
        {
            repositorio.Filas[NOTA] = new FilaNotaEntregaCabecera { Empresa = EMPRESA, Pedido = NOTA, Tipo = "Recogida", Bultos = 1, PedidoAbono = 930600 };

            _ = await servicio.Actualizar(EMPRESA, NOTA, Peticion("Recogida", 4), "carlos");

            Assert.AreEqual(930600, repositorio.Guardadas.Single().PedidoAbono);
        }

        // ================================================================ POST api/PedidosVenta: crear la nota

        [TestMethod]
        public async Task PrepararAlCrear_QuienNoEstaEnLosGrupos_SigueSinCrearNotas()
        {
            PedidoVentaDTO pedido = Nota("Recogida", 3);

            string motivo = await servicio.PrepararAlCrear(pedido, Usuario("vendedor", "Ventas"));

            Assert.IsNull(motivo);
            Assert.IsFalse(pedido.notaEntrega, "como hasta ahora: el POST no crea notas para los demás");
            Assert.IsNull(pedido.tipoNotaEntrega);
            Assert.IsNull(pedido.bultosNota);
        }

        [TestMethod]
        public async Task PrepararAlCrear_SinUsuario_SigueSinCrearNotas()
        {
            PedidoVentaDTO pedido = Nota("Entrega", null);

            Assert.IsNull(await servicio.PrepararAlCrear(pedido, null));
            Assert.IsFalse(pedido.notaEntrega);
        }

        [TestMethod]
        public async Task PrepararAlCrear_PedidoNormal_NoTocaNada()
        {
            PedidoVentaDTO pedido = Nota(null, null);
            pedido.notaEntrega = false;
            pedido.periodoFacturacion = "FDM";
            pedido.modoFacturacion = 2;

            Assert.IsNull(await servicio.PrepararAlCrear(pedido, Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)));
            Assert.AreEqual("FDM", pedido.periodoFacturacion);
            Assert.AreEqual((byte?)2, pedido.modoFacturacion);
        }

        [TestMethod]
        public async Task PrepararAlCrear_AlmacenConRecogida_LaDejaCoherente()
        {
            PedidoVentaDTO pedido = Nota("recogida", 3);
            pedido.periodoFacturacion = "FDM";
            pedido.modoFacturacion = 2;
            pedido.mantenerJunto = true;
            pedido.servirPendienteAlHaberStock = true;

            string motivo = await servicio.PrepararAlCrear(pedido, Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN));

            Assert.IsNull(motivo);
            Assert.IsTrue(pedido.notaEntrega);
            Assert.AreEqual("Recogida", pedido.tipoNotaEntrega);
            Assert.AreEqual((short?)3, pedido.bultosNota);
            Assert.AreEqual(Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, pedido.periodoFacturacion);
            Assert.IsNull(pedido.modoFacturacion, "una nota no tiene modo de facturación");
            Assert.IsFalse(pedido.mantenerJunto);
            Assert.IsNull(pedido.servirPendienteAlHaberStock);
        }

        [TestMethod]
        public async Task PrepararAlCrear_CadaGrupoPermitido_PuedeCrearNotas()
        {
            foreach (string grupo in new[] { Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.ADMINISTRACION,
                Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA })
            {
                PedidoVentaDTO pedido = Nota(null, null);
                Assert.IsNull(await servicio.PrepararAlCrear(pedido, Usuario("x", "NUEVAVISION\\" + grupo)), grupo);
                Assert.IsTrue(pedido.notaEntrega, grupo);
                Assert.AreEqual("Entrega", pedido.tipoNotaEntrega, "sin tipo, Entrega");
            }
        }

        [TestMethod]
        public async Task PrepararAlCrear_EntregaConCeroBultos_NoSeValida()
        {
            PedidoVentaDTO pedido = Nota("Entrega", 0);

            Assert.IsNull(await servicio.PrepararAlCrear(pedido, Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)));
            Assert.AreEqual((short?)1, pedido.bultosNota);
        }

        [TestMethod]
        public async Task PrepararAlCrear_RecogidaConCeroBultos_400()
        {
            string motivo = await servicio.PrepararAlCrear(Nota("Recogida", 0), Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN));

            StringAssert.Contains(motivo, "al menos un bulto");
        }

        [TestMethod]
        public async Task PrepararAlCrear_RecogidaConFacturaDeOtroCliente_400()
        {
            PedidoVentaDTO pedido = Nota("Recogida", 1);
            pedido.facturaOriginalNota = "NV2600999";

            StringAssert.Contains(await servicio.PrepararAlCrear(pedido, Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)), "es del cliente 9999");
        }

        [TestMethod]
        public async Task PrepararAlCrear_TipoQueNoExiste_400()
        {
            StringAssert.Contains(await servicio.PrepararAlCrear(Nota("Otro", 1), Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)),
                "«Entrega» o «Recogida»");
        }

        [TestMethod]
        public async Task PrepararAlCrear_ConLineaDeCuentaContable_400()
        {
            PedidoVentaDTO pedido = Nota("Entrega", null);
            pedido.Lineas.Add(new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE, Producto = "62400003", Cantidad = 1 });

            string motivo = await servicio.PrepararAlCrear(pedido, Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN));

            StringAssert.Contains(motivo, "solo puede llevar líneas de texto o de producto");
            StringAssert.Contains(motivo, "62400003");
        }

        [TestMethod]
        public async Task PrepararAlCrear_SoloTexto_Vale()
        {
            PedidoVentaDTO pedido = Nota("Entrega", null);
            pedido.Lineas.Clear();
            pedido.Lineas.Add(new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.TEXTO, texto = "PRÉSTAMO DE APARATO" });

            Assert.IsNull(await servicio.PrepararAlCrear(pedido, Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)));
        }

        [TestMethod]
        public async Task PrepararAlCrear_RecogidaSinScript_400()
        {
            repositorio.Script = false;

            StringAssert.Contains(await servicio.PrepararAlCrear(Nota("Recogida", 1), Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)),
                "Issue757_NotasEntregaCabecera.sql");
        }

        [TestMethod]
        public async Task PrepararAlCrear_EntregaSinScript_Vale()
        {
            repositorio.Script = false;

            Assert.IsNull(await servicio.PrepararAlCrear(Nota("Entrega", null), Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN)));
        }

        [TestMethod]
        public async Task GuardarAlCrear_NotaConTipo_GuardaLaFila()
        {
            PedidoVentaDTO pedido = Nota("Recogida", 3);
            pedido.numero = 930777;
            _ = await servicio.PrepararAlCrear(pedido, Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN));

            await servicio.GuardarAlCrear(pedido, "alfredo");

            FilaNotaEntregaCabecera fila = repositorio.Guardadas.Single();
            Assert.AreEqual(EMPRESA, fila.Empresa);
            Assert.AreEqual(930777, fila.Pedido);
            Assert.AreEqual("Recogida", fila.Tipo);
            Assert.AreEqual(3, fila.Bultos);
            Assert.AreEqual("alfredo", fila.Usuario);
            Assert.AreEqual(0, repositorio.Modificaciones.Count, "al crear no hay «anterior»");
        }

        [TestMethod]
        public async Task GuardarAlCrear_PedidoNormal_NoGuardaNada()
        {
            PedidoVentaDTO pedido = Nota("Recogida", 3);
            pedido.notaEntrega = false;

            await servicio.GuardarAlCrear(pedido, "alfredo");

            Assert.AreEqual(0, repositorio.Guardadas.Count);
        }

        [TestMethod]
        public async Task GuardarAlCrear_SiFallaLaBd_NoLanzaYLoRegistra()
        {
            repositorio.FallarAlGuardar = true;
            PedidoVentaDTO pedido = Nota("Recogida", 3);

            await servicio.GuardarAlCrear(pedido, "alfredo");

            StringAssert.Contains(registrados.Single().Message, "#757");
        }

        [TestMethod]
        public async Task PostPedidoVenta_AlmacenConNotaYPortes_400ConElMotivo()
        {
            // El POST de pedidos llama a PrepararAlCrear antes de tocar la base de datos.
            NVEntities db = A.Fake<NVEntities>();
            IServicioCursosPlantilla cursos = A.Fake<IServicioCursosPlantilla>();
            _ = A.CallTo(() => cursos.Preparar(A<PedidoVentaDTO>._)).Returns(Task.FromResult(new PreparacionCursos()));
            var controlador = new PedidosVentaController(db)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN),
                ServicioCursosPlantilla = cursos,
                ServicioNotasEntregaCabecera = servicio
            };
            PedidoVentaDTO pedido = Nota("Entrega", null);
            pedido.Lineas.Add(new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE, Producto = "62400003", Cantidad = 1 });

            IHttpActionResult respuesta = await controlador.PostPedidoVenta(pedido);

            var malo = respuesta as BadRequestErrorMessageResult;
            Assert.IsNotNull(malo, respuesta?.GetType().Name);
            StringAssert.Contains(malo.Message, "solo puede llevar líneas de texto o de producto");
        }

        // ================================================================ GET del pedido: campos de solo lectura

        [TestMethod]
        public void RellenarCamposPedido_NotaSinFila_EntregaYUnBulto()
        {
            var pedido = new PedidoVentaDTO { notaEntrega = true };

            ServicioNotasEntregaCabecera.RellenarCamposPedido(pedido, null);

            Assert.AreEqual("Entrega", pedido.tipoNotaEntrega);
            Assert.AreEqual((short?)1, pedido.bultosNota);
            Assert.IsNull(pedido.facturaOriginalNota);
        }

        [TestMethod]
        public void RellenarCamposPedido_NotaConFila_TraeSusCampos()
        {
            var pedido = new PedidoVentaDTO { notaEntrega = true };

            ServicioNotasEntregaCabecera.RellenarCamposPedido(pedido,
                new FilaNotaEntregaCabecera { Tipo = "Recogida", Bultos = 4, FacturaOriginal = "NV2600123 " });

            Assert.AreEqual("Recogida", pedido.tipoNotaEntrega);
            Assert.AreEqual((short?)4, pedido.bultosNota);
            Assert.AreEqual("NV2600123", pedido.facturaOriginalNota);
        }

        [TestMethod]
        public void RellenarCamposPedido_PedidoNormal_TodoNull()
        {
            var pedido = new PedidoVentaDTO { notaEntrega = false, tipoNotaEntrega = "Recogida", bultosNota = 2 };

            ServicioNotasEntregaCabecera.RellenarCamposPedido(pedido, null);

            Assert.IsNull(pedido.tipoNotaEntrega);
            Assert.IsNull(pedido.bultosNota);
            Assert.IsNull(pedido.facturaOriginalNota);
        }

        // ================================================================ controlador: permisos y códigos

        [TestMethod]
        public async Task Controlador_Get_QuienNoEstaEnLosGrupos_403()
        {
            NotasEntregaController controlador = Controlador(Usuario("vendedor", "Ventas"));

            HttpResponseMessage respuesta = await Ejecutar(await controlador.GetNotaEntrega(EMPRESA, NOTA));

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "no tiene permiso");
        }

        [TestMethod]
        public async Task Controlador_Put_QuienNoEstaEnLosGrupos_403YNoGuarda()
        {
            NotasEntregaController controlador = Controlador(Usuario("tienda", Constantes.GruposSeguridad.TIENDAS));

            HttpResponseMessage respuesta = await Ejecutar(await controlador.PutCabecera(EMPRESA, NOTA, Peticion("Recogida", 2)));

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual(0, repositorio.Guardadas.Count);
        }

        [TestMethod]
        public async Task Controlador_Get_Informatica_200ConLaCabecera()
        {
            NotasEntregaController controlador = Controlador(Usuario("carlos", Constantes.GruposSeguridad.INFORMATICA));

            IHttpActionResult resultado = await controlador.GetNotaEntrega(EMPRESA, NOTA);

            var ok = resultado as OkNegotiatedContentResult<NotaEntregaCabeceraDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual("NE930500", ok.Content.CodigoDocumento);
        }

        [TestMethod]
        public async Task Controlador_Get_NoEsNota_404()
        {
            NotasEntregaController controlador = Controlador(Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN));

            Assert.AreEqual(HttpStatusCode.NotFound, (await Ejecutar(await controlador.GetNotaEntrega(EMPRESA, 930501))).StatusCode);
        }

        [TestMethod]
        public async Task Controlador_Put_Validaciones400YEntregado409()
        {
            NotasEntregaController controlador = Controlador(Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN));

            HttpResponseMessage cero = await Ejecutar(await controlador.PutCabecera(EMPRESA, NOTA, Peticion("Recogida", 0)));
            Assert.AreEqual(HttpStatusCode.BadRequest, cero.StatusCode);
            StringAssert.Contains(await cero.Content.ReadAsStringAsync(), "al menos un bulto");

            repositorio.Envio = new EnvioNotaEntrega { Numero = 251400, Estado = Constantes.Agencias.ESTADO_ENTREGADO };
            Assert.AreEqual(HttpStatusCode.Conflict, (await Ejecutar(await controlador.PutCabecera(EMPRESA, NOTA, Peticion("Recogida", 2)))).StatusCode);
        }

        [TestMethod]
        public async Task Controlador_Put_GuardaConElUsuarioDelToken()
        {
            NotasEntregaController controlador = Controlador(Usuario("Alfredo", Constantes.GruposSeguridad.ALMACEN));

            IHttpActionResult resultado = await controlador.PutCabecera(EMPRESA, NOTA, Peticion("Recogida", 2));

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<NotaEntregaCabeceraDTO>));
            Assert.AreEqual("Alfredo", repositorio.Guardadas.Single().Usuario);
        }

        // ================================================================ ayudantes

        private NotasEntregaController Controlador(IPrincipal usuario) => new NotasEntregaController(null, servicio)
        {
            Request = new HttpRequestMessage(),
            Configuration = new HttpConfiguration(),
            User = usuario
        };

        private static Task<HttpResponseMessage> Ejecutar(IHttpActionResult resultado) => resultado.ExecuteAsync(CancellationToken.None);

        private static ClaimsPrincipal Usuario(string nombre, string grupo) => new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, nombre), new Claim(ClaimTypes.Role, grupo)
        }, "prueba"));

        private static ActualizarNotaEntregaCabeceraDTO Peticion(string tipo, short? bultos, string factura = null)
            => new ActualizarNotaEntregaCabeceraDTO { Tipo = tipo, Bultos = bultos, FacturaOriginal = factura };

        private static PedidoVentaDTO Nota(string tipo, short? bultos) => new PedidoVentaDTO
        {
            empresa = EMPRESA,
            cliente = CLIENTE,
            contacto = "0",
            Usuario = "NUEVAVISION\\alfredo",
            fecha = DateTime.Today,
            formaPago = Constantes.FormasPago.TARJETA,
            plazosPago = Constantes.PlazosPago.PREPAGO,
            notaEntrega = true,
            tipoNotaEntrega = tipo,
            bultosNota = bultos,
            Lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "12345", Cantidad = 1 }
            }
        };

        private class RepositorioFalso : IRepositorioNotasEntregaCabecera
        {
            public bool Script = true;
            public bool FallarAlGuardar;
            public readonly Dictionary<int, PedidoNotaEntrega> Pedidos = new Dictionary<int, PedidoNotaEntrega>();
            public readonly Dictionary<int, FilaNotaEntregaCabecera> Filas = new Dictionary<int, FilaNotaEntregaCabecera>();
            public readonly Dictionary<string, string> Facturas = new Dictionary<string, string>();
            public readonly List<FilaNotaEntregaCabecera> Guardadas = new List<FilaNotaEntregaCabecera>();
            public readonly List<Modificacion> Modificaciones = new List<Modificacion>();
            public int? NumeroNota;
            public bool Procesada;
            public EnvioNotaEntrega Envio;

            public bool ScriptAplicado() => Script;

            public Task<FilaNotaEntregaCabecera> Leer(string empresa, int pedido)
                => Task.FromResult(Script && Filas.TryGetValue(pedido, out FilaNotaEntregaCabecera fila) ? fila : null);

            public Task Guardar(FilaNotaEntregaCabecera fila, Modificacion modificacion)
            {
                if (FallarAlGuardar)
                {
                    throw new InvalidOperationException("se denegó el permiso");
                }
                Guardadas.Add(fila);
                Filas[fila.Pedido] = fila;
                if (modificacion != null)
                {
                    Modificaciones.Add(modificacion);
                }
                return Task.CompletedTask;
            }

            public Task<PedidoNotaEntrega> LeerPedido(string empresa, int pedido)
                => Task.FromResult(Pedidos.TryGetValue(pedido, out PedidoNotaEntrega p) ? p : null);

            public Task<int?> LeerNumeroNota(string empresa, int pedido) => Task.FromResult(NumeroNota);

            public Task<bool> TieneLineasProcesadas(string empresa, int pedido) => Task.FromResult(Procesada);

            public Task<EnvioNotaEntrega> LeerUltimoEnvio(string empresa, int pedido) => Task.FromResult(Envio);

            public Task<string> LeerClienteFactura(string empresa, string factura)
                => Task.FromResult(Facturas.TryGetValue(factura, out string cliente) ? cliente : null);
        }
    }
}
