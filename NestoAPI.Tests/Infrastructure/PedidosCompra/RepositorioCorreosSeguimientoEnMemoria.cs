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
            Correos.Add(copia);
            return Task.FromResult(true);
        }

        public Task<List<RemitenteMarcado>> LeerRemitentes() => Task.FromResult(new List<RemitenteMarcado>());
        public Task<int> GuardarRemitente(string agencia, string remitente, string usuario) => Task.FromResult(1);
        public Task<bool> DesactivarRemitente(int id, string usuario) => Task.FromResult(true);
        public Task<List<PedidoCompraEnAire>> LeerPedidosEnAire(string empresa) =>
            Task.FromResult(Pedidos.Where(p => Lineas.TryGetValue(p.Pedido, out var l) && l.Any()).Select(p => p).ToList());
        public Task<List<CorreoSeguimientoFila>> LeerPendientesDeMedir(DateTime desde) => Task.FromResult(new List<CorreoSeguimientoFila>());
        public Task<List<AlbaranPedidoCompra>> LeerAlbaranes(IEnumerable<CandidatoPedidoCompra> pedidos) => Task.FromResult(new List<AlbaranPedidoCompra>());
        public Task GuardarReal(int id, AlbaranPedidoCompra albaran) => Task.CompletedTask;

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
            List<LineaCompraCorreo> lineas = Lineas.TryGetValue(peticion.Pedido, out var l) ? l : new List<LineaCompraCorreo>();
            resultado.LineasAntes = lineas.Select(x => new LineaCompraCorreo
            {
                NumeroOrden = x.NumeroOrden, Producto = x.Producto, Estado = x.Estado, Enviado = x.Enviado, Cantidad = x.Cantidad, FechaRecepcion = x.FechaRecepcion, Proveedor = x.Proveedor
            }).ToList();
            resultado.Proveedor = lineas.Select(x => x.Proveedor).FirstOrDefault();
            EstadoAplicacionFecha? bloqueo = ReglaAplicacionCorreo.Bloqueo(correo.EmpresaAplicada, correo.PedidoAplicado, peticion.Empresa, peticion.Pedido, peticion.Reasignar);
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
    }
}
