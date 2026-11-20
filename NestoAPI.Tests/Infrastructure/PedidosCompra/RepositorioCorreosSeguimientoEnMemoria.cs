using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#857 (fase 3): repositorio en memoria (con las líneas de compra) para probar el servicio de punta a punta sin BD.</summary>
    internal class RepositorioCorreosSeguimientoEnMemoria : IRepositorioCorreosSeguimiento
    {
        public bool ConAplicacion { get; set; } = true;
        public List<CorreoSeguimientoFila> Correos { get; } = new List<CorreoSeguimientoFila>();
        public List<PedidoCompraEnAire> Pedidos { get; } = new List<PedidoCompraEnAire>();
        public Dictionary<int, List<LineaCompraCorreo>> Lineas { get; } = new Dictionary<int, List<LineaCompraCorreo>>();
        /// <summary>Las filas de Modificaciones de la aplicación de fechas (Tabla CompraFechaPorCorreo).</summary>
        public List<PeticionAplicacionFecha> Rastros { get; } = new List<PeticionAplicacionFecha>();
        public List<RemitentePaqueteRecordado> RemitentesPaquete { get; } = new List<RemitentePaqueteRecordado>();
        public int LlamadasAplicar { get; private set; }
        private int siguienteId = 1;

        private static CorreoSeguimientoFila Copia(CorreoSeguimientoFila f) =>
            JsonConvert.DeserializeObject<CorreoSeguimientoFila>(JsonConvert.SerializeObject(f));

        public void AnadirPedido(int pedido, string proveedor, string nombre, DateTime fechaEnvio, params DateTime?[] fechasLineas)
        {
            Pedidos.Add(new PedidoCompraEnAire
            {
                Empresa = "1", Pedido = pedido, Proveedor = proveedor, NombreProveedor = nombre, FechaEnvio = fechaEnvio, LineasPendientes = fechasLineas.Length
            });
            Lineas[pedido] = fechasLineas.Select((f, i) => new LineaCompraCorreo
            {
                NumeroOrden = pedido * 10 + i, Producto = "P" + i, Estado = 1, Enviado = true, Cantidad = 1, FechaRecepcion = f, Proveedor = proveedor
            }).ToList();
        }

        public CorreoSeguimientoFila Correo(int id) => Correos.Single(c => c.Id == id);

        public bool TablasExisten() => true;
        public bool AdjuntosDisponibles() => false;
        public bool AplicacionDisponible() => ConAplicacion;
        public Task<List<PedidoCompraEnAire>> LeerPedidosDesde(string empresa, DateTime desde) => Task.FromResult(Pedidos.ToList());
        public Task<List<CorreoSeguimientoFila>> LeerPendientesDeMedirPorId(DateTime desde, int despuesDeId, int cuantos) => Task.FromResult(new List<CorreoSeguimientoFila>());
        public Task<string> LeerEstado(string buzon) => Task.FromResult<string>(null);
        public Task GuardarEstado(string buzon, string enlace, string ultimoError) => Task.CompletedTask;

        public Task<bool> Insertar(CorreoSeguimientoFila fila)
        {
            if (Correos.Any(c => c.MensajeId == fila.MensajeId))
            {
                return Task.FromResult(false);
            }
            CorreoSeguimientoFila copia = Copia(fila);
            copia.Id = siguienteId++;
            copia.FechaCaptura = copia.Recibido; // en las pruebas, capturado al llegar
            if (!ConAplicacion)
            {
                copia.RemitentePaquete = null;
            }
            if (!ConLineas)
            {
                copia.LineasAviso = null;
            }
            Correos.Add(copia);
            return Task.FromResult(true);
        }

        /// <summary>NestoAPI#857e: los remitentes marcados (vacía = como antes).</summary>
        public List<RemitenteMarcado> Remitentes { get; } = new List<RemitenteMarcado>();
        public Task<List<RemitenteMarcado>> LeerRemitentes() => Task.FromResult(Remitentes.ToList());
        public Task<int> GuardarRemitente(string agencia, string remitente, string usuario, string filtroTexto, bool leerPdf, bool ignorar) => Task.FromResult(1);

        /// <summary>NestoAPI#857e: los remitentes ignorados al descartar (remitente, usuario, correo).</summary>
        public List<(string Remitente, string Usuario, int CorreoId)> Ignorados { get; } = new List<(string, string, int)>();

        public Task<int> IgnorarRemitente(string remitente, string usuario, int correoId)
        {
            Ignorados.Add((remitente, usuario, correoId));
            RemitenteMarcado existente = Remitentes.FirstOrDefault(r => r.Remitente == remitente);
            if (existente == null)
            {
                Remitentes.Add(existente = new RemitenteMarcado { Id = Remitentes.Count + 100, Remitente = remitente });
            }
            existente.Activo = true;
            existente.Ignorar = true;
            existente.Usuario = usuario;
            return Task.FromResult(existente.Id);
        }
        public bool AvisosProveedorDisponibles() => true;
        public Task<bool> DesactivarRemitente(int id, string usuario) => Task.FromResult(true);
        public Task<List<PedidoCompraEnAire>> LeerPedidosEnAire(string empresa) =>
            Task.FromResult(Pedidos.Where(p => Lineas.TryGetValue(p.Pedido, out var l) && l.Any()).Select(p => p).ToList());
        /// <summary>Los albaranes de los pedidos (para la medición, PedidoReal). Vacío = como antes: no se mide nada.</summary>
        public List<AlbaranPedidoCompra> Albaranes { get; } = new List<AlbaranPedidoCompra>();

        /// <summary>El pedido se recibe entero ese día: sale del aire y tiene albarán.</summary>
        public void Recibir(int pedido, DateTime fechaAlbaran)
        {
            Lineas[pedido] = new List<LineaCompraCorreo>();
            Albaranes.Add(new AlbaranPedidoCompra { Empresa = "1", Pedido = pedido, FechaAlbaran = fechaAlbaran });
        }

        public Task<List<CorreoSeguimientoFila>> LeerPendientesDeMedir(DateTime desde) => Task.FromResult(Correos
            .Where(c => !c.PedidoReal.HasValue && c.PedidosCandidatos != null && c.Recibido >= desde).Select(Copia).ToList());
        public Task<List<AlbaranPedidoCompra>> LeerAlbaranes(IEnumerable<CandidatoPedidoCompra> pedidos)
        {
            var claves = new HashSet<string>((pedidos ?? Enumerable.Empty<CandidatoPedidoCompra>()).Select(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido)));
            return Task.FromResult(Albaranes.Where(a => claves.Contains(AsignadorPedidosCompra.Clave(a.Empresa, a.Pedido))).ToList());
        }
        public Task GuardarReal(int id, AlbaranPedidoCompra albaran)
        {
            CorreoSeguimientoFila fila = Correos.Single(c => c.Id == id);
            if (!fila.PedidoReal.HasValue)
            {
                fila.EmpresaReal = albaran.Empresa;
                fila.PedidoReal = albaran.Pedido;
                fila.FechaAlbaranReal = albaran.FechaAlbaran.Date;
            }
            return Task.CompletedTask;
        }

        public Task<List<CorreoSeguimientoFila>> LeerCorreos(DateTime desde) =>
            Task.FromResult(Correos.Where(c => c.Recibido >= desde).OrderByDescending(c => c.Recibido).Select(Copia).ToList());

        public Task<int?> IdDe(string mensajeId) => Task.FromResult(Correos.FirstOrDefault(c => c.MensajeId == mensajeId)?.Id);
        public Task<CorreoSeguimientoFila> LeerCorreo(int id) => Task.FromResult(Correos.Where(c => c.Id == id).Select(Copia).FirstOrDefault());

        public Task<ResultadoAplicacionFecha> AplicarFecha(PeticionAplicacionFecha peticion)
        {
            LlamadasAplicar++;
            var resultado = new ResultadoAplicacionFecha();
            CorreoSeguimientoFila correo = Correos.FirstOrDefault(c => c.Id == peticion.CorreoId);
            if (correo == null)
            {
                resultado.Estado = EstadoAplicacionFecha.NoExisteCorreo;
                return Task.FromResult(resultado);
            }
            resultado.EmpresaAnterior = correo.EmpresaAplicada;
            resultado.PedidoAnterior = correo.PedidoAplicado;
            List<LineaCompraCorreo> lineas = ReglaAplicacionCorreo.DelAviso(Lineas.TryGetValue(peticion.Pedido, out var l) ? l : new List<LineaCompraCorreo>(),
                peticion.Ordenes);
            resultado.LineasAntes = lineas.Select(x => new LineaCompraCorreo
            {
                NumeroOrden = x.NumeroOrden, Producto = x.Producto, Estado = x.Estado, Enviado = x.Enviado, Cantidad = x.Cantidad, FechaRecepcion = x.FechaRecepcion, Proveedor = x.Proveedor
            }).ToList();
            resultado.Proveedor = lineas.Select(x => x.Proveedor).FirstOrDefault();
            EstadoAplicacionFecha? bloqueo = ReglaAplicacionCorreo.Bloqueo(correo.EmpresaAplicada, correo.PedidoAplicado, peticion.Empresa, peticion.Pedido, peticion.Reasignar, peticion.Corregir);
            if (bloqueo.HasValue)
            {
                resultado.Estado = bloqueo.Value;
                return Task.FromResult(resultado);
            }
            if (!lineas.Any())
            {
                resultado.Estado = EstadoAplicacionFecha.NoEnElAire;
                return Task.FromResult(resultado);
            }
            List<LineaCompraCorreo> cambian = ReglaAplicacionCorreo.QueCambian(lineas, peticion.Fecha);
            foreach (LineaCompraCorreo linea in cambian)
            {
                linea.FechaRecepcion = peticion.Fecha.Date;
            }
            resultado.LineasCambiadas = cambian.Count;
            if (cambian.Any())
            {
                Rastros.Add(peticion);
            }
            correo.EmpresaAplicada = peticion.Empresa;
            correo.PedidoAplicado = peticion.Pedido;
            correo.FechaRecepcionAplicada = peticion.Fecha.Date;
            correo.AplicadoEl = DateTime.Now;
            correo.AplicadoPor = peticion.AplicadoPor;
            correo.Descartado = false;
            correo.EligioSugerido = peticion.EligioSugerido;
            correo.LineasAplicadas = ConLineas && peticion.Ordenes != null ? LineasAvisoCorreo.SerializarAplicadas(peticion.LineasCasadas) : null;
            resultado.Estado = EstadoAplicacionFecha.Aplicada;
            return Task.FromResult(resultado);
        }

        public Task<CorreoSeguimientoFila> Descartar(int id, string usuario)
        {
            CorreoSeguimientoFila correo = Correos.FirstOrDefault(c => c.Id == id);
            if (correo == null)
            {
                return Task.FromResult<CorreoSeguimientoFila>(null);
            }
            CorreoSeguimientoFila antes = Copia(correo);
            if (!correo.PedidoAplicado.HasValue)
            {
                correo.Descartado = true;
                correo.AplicadoPor = usuario;
            }
            return Task.FromResult(antes);
        }

        public Task<List<RemitentePaqueteRecordado>> LeerRemitentesPaquete() => Task.FromResult(RemitentesPaquete.ToList());

        public Task<int> GuardarRemitentePaquete(string remitente, string empresa, string proveedor, string usuario)
        {
            RemitentePaqueteRecordado r = RemitentesPaquete.FirstOrDefault(x => x.RemitentePaquete == remitente);
            if (r == null)
            {
                r = new RemitentePaqueteRecordado { Id = RemitentesPaquete.Count + 1, RemitentePaquete = remitente };
                RemitentesPaquete.Add(r);
            }
            r.Empresa = empresa;
            r.Proveedor = proveedor;
            r.Usuario = usuario;
            return Task.FromResult(r.Id);
        }

        public Task<bool> BorrarRemitentePaquete(int id, string usuario) => Task.FromResult(RemitentesPaquete.RemoveAll(r => r.Id == id) > 0);

        public Task<bool> ActualizarAnalisis(CorreoSeguimientoFila fila)
        {
            CorreoSeguimientoFila correo = Correos.FirstOrDefault(c => c.MensajeId == fila.MensajeId && !c.PedidoAplicado.HasValue && c.Descartado != true);
            if (correo == null)
            {
                return Task.FromResult(false);
            }
            correo.Agencia = fila.Agencia;
            correo.Codigo = fila.Codigo;
            correo.Url = fila.Url;
            correo.Seguimientos = fila.Seguimientos;
            correo.Extracto = fila.Extracto;
            correo.ProveedorPropuesto = fila.ProveedorPropuesto;
            correo.PedidosCandidatos = fila.PedidosCandidatos;
            correo.RemitentePaquete = fila.RemitentePaquete;
            correo.EntregaEstimadaDia = fila.EntregaEstimadaDia;
            correo.EsSalidaNuestra = fila.EsSalidaNuestra;
            correo.AvisoProveedor = fila.AvisoProveedor;
            correo.PedidoEnAdjunto = fila.PedidoEnAdjunto;
            correo.AdjuntoLeido = fila.AdjuntoLeido;
            correo.EstadoEnvio = fila.EstadoEnvio;
            correo.OrigenEntrega = fila.OrigenEntrega;
            if (ConLineas)
            {
                correo.LineasAviso = fila.LineasAviso;
            }
            return Task.FromResult(true);
        }

        public Task<bool> ActualizarAsignacion(int id, string proveedor, string candidatos)
        {
            CorreoSeguimientoFila correo = Correos.FirstOrDefault(c => c.Id == id && !c.PedidoAplicado.HasValue && c.Descartado != true);
            if (correo == null)
            {
                return Task.FromResult(false);
            }
            correo.ProveedorPropuesto = proveedor;
            correo.PedidosCandidatos = candidatos;
            return Task.FromResult(true);
        }

        public Task MarcarAvisado(int id)
        {
            Correos.Single(c => c.Id == id).AvisadoPorAsignar = DateTime.Now;
            return Task.CompletedTask;
        }

        /// <summary>NestoAPI#857f: con las columnas de Issue857f (por defecto, sí).</summary>
        public bool ConEstado { get; set; } = true;
        public bool EstadoEnvioDisponible() => ConEstado;

        public Task GuardarConsulta(int id, EntregaAgencia respuesta, DateTime consultadaEl)
        {
            CorreoSeguimientoFila correo = Correos.Single(c => c.Id == id);
            correo.EstadoEnvio = respuesta?.Estado?.ToString() ?? correo.EstadoEnvio;
            correo.EntregaConsultadaEl = consultadaEl;
            EntregaEstimada entrega = respuesta?.Estado == EstadoEnvioAgencia.Entregado ? null : respuesta?.Entrega;
            if (entrega != null)
            {
                correo.EntregaEstimadaDia = entrega.Dia.Date;
                correo.EntregaEstimadaDesde = entrega.Desde;
                correo.EntregaEstimadaHasta = entrega.Hasta;
                correo.OrigenEntrega = OrigenEntregaCorreo.CONSULTA;
            }
            return Task.CompletedTask;
        }

        /// <summary>NestoAPI#857g: con la tabla de Issue857g (por defecto, sí).</summary>
        public bool ConConfirmaciones { get; set; } = true;
        public List<ConfirmacionPedidoProveedorFila> Confirmaciones { get; } = new List<ConfirmacionPedidoProveedorFila>();
        public bool ConfirmacionesDisponibles() => ConConfirmaciones;

        public Task<bool> GuardarConfirmacion(ConfirmacionPedidoProveedorFila fila)
        {
            ConfirmacionPedidoProveedorFila ya = Confirmaciones.FirstOrDefault(c => c.MensajeId == fila.MensajeId);
            if (ya != null)
            {
                Confirmaciones.Remove(ya);
                fila.Id = ya.Id;
            }
            else
            {
                fila.Id = Confirmaciones.Count + 1;
            }
            Confirmaciones.Add(fila);
            return Task.FromResult(true);
        }

        public Task<List<ConfirmacionPedidoProveedorFila>> LeerConfirmaciones(DateTime desde) =>
            Task.FromResult(Confirmaciones.Where(c => c.Recibido >= desde).ToList());

        /// <summary>20/11/26: con las columnas de Issue857h (por defecto, sí).</summary>
        public bool ConLineas { get; set; } = true;
        /// <summary>ProveedoresProducto de las pruebas.</summary>
        public List<ReferenciaProductoProveedor> Referencias { get; } = new List<ReferenciaProductoProveedor>();
        public int LecturasLineasAviso { get; private set; }
        public bool LineasAvisoDisponibles() => ConLineas;

        public Task<DatosLineasAviso> LeerLineasAviso(IEnumerable<string> proveedores)
        {
            LecturasLineasAviso++;
            var suyos = new HashSet<string>(proveedores ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(Datos(Pedidos.Where(p => suyos.Contains(p.Proveedor))));
        }

        public Task<DatosLineasAviso> LeerLineasAvisoDelPedido(string empresa, int pedido) =>
            Task.FromResult(Datos(Pedidos.Where(p => p.Pedido == pedido)));

        private DatosLineasAviso Datos(IEnumerable<PedidoCompraEnAire> pedidos)
        {
            List<PedidoCompraEnAire> lista = pedidos.ToList();
            var proveedores = new HashSet<string>(lista.Select(p => p.Proveedor), StringComparer.OrdinalIgnoreCase);
            return new DatosLineasAviso
            {
                Lineas = lista.SelectMany(p => (Lineas.TryGetValue(p.Pedido, out var l) ? l : new List<LineaCompraCorreo>()).Select(x => new LineaEnAireAviso
                {
                    Empresa = p.Empresa, Pedido = p.Pedido, NumeroOrden = x.NumeroOrden, Producto = x.Producto, Cantidad = x.Cantidad, Proveedor = p.Proveedor
                })).ToList(),
                Referencias = Referencias.Where(r => proveedores.Contains(r.Proveedor)).ToList()
            };
        }
    }
}
