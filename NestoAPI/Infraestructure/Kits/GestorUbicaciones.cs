using NestoAPI.Models.Kits;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Kits
{
    public class GestorUbicaciones
    {
        private readonly IUbicacionService _servicioUbicacion;

        public GestorUbicaciones(IUbicacionService servicioUbicacion)
        {
            _servicioUbicacion = servicioUbicacion;
        }

        public async Task<List<PreExtractoProductoDTO>> AsignarUbicacionesMasAntiguas(List<PreExtractoProductoDTO> preExtractos)
        {
            foreach (var preExtracto in preExtractos)
            {
                var ubicaciones = await _servicioUbicacion.LeerUbicacionesProducto(preExtracto.Producto);
                if (preExtracto.Cantidad < 0 && (ubicaciones == null || !ubicaciones.Any()))
                {
                    throw new Exception($"El producto {preExtracto.Producto} no tiene stock");
                }
                foreach (var ubicacion in ubicaciones)
                {
                    if (preExtracto.CantidadPendiente == 0)
                    {
                        break;
                    }
                    if (ubicacion.Cantidad == -preExtracto.CantidadPendiente)
                    {
                        ubicacion.Cantidad = preExtracto.CantidadPendiente;
                        ubicacion.Estado = Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS;
                        preExtracto.Ubicaciones.Add(ubicacion);
                    }
                    else if (ubicacion.Cantidad > -preExtracto.CantidadPendiente)
                    {
                        var nuevaUbicacion = ubicacion.Clone();
                        nuevaUbicacion.Cantidad = preExtracto.CantidadPendiente;
                        nuevaUbicacion.Estado = Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS;
                        preExtracto.Ubicaciones.Add(nuevaUbicacion);
                        ubicacion.Cantidad += nuevaUbicacion.Cantidad;
                        ubicacion.Estado = Constantes.Ubicaciones.ESTADO_A_MODIFICAR_CANTIDAD;
                        preExtracto.Ubicaciones.Add(ubicacion);
                    }
                    else // es menor
                    {
                        if (ubicacion == ubicaciones.Last())
                        {
                            throw new Exception("No hay cantidad suficiente para montar el kit");
                        }
                        ubicacion.Cantidad = -ubicacion.Cantidad;
                        ubicacion.Estado = Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS;
                        preExtracto.Ubicaciones.Add(ubicacion);
                    }
                }
                if (!ubicaciones.Any() && preExtracto.Cantidad > 0)
                {
                    // Dejamos una pendiente de ubicar
                    var nuevaUbicacion = new UbicacionProductoDTO()
                    {
                        Id = 0,
                        Empresa = preExtracto.Empresa,
                        Almacen = preExtracto.Almacen,
                        Producto = preExtracto.Producto,
                        Cantidad = preExtracto.Cantidad,
                        Estado = Constantes.Ubicaciones.PENDIENTE_UBICAR
                    };
                    preExtracto.Ubicaciones.Add(nuevaUbicacion);
                    // Y otra para el registro
                    var nuevaUbicacionRegistro = new UbicacionProductoDTO()
                    {
                        Id = 0,
                        Empresa = preExtracto.Empresa,
                        Almacen = preExtracto.Almacen,
                        Producto = preExtracto.Producto,
                        Cantidad = preExtracto.Cantidad,
                        Estado = Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS
                    };
                    preExtracto.Ubicaciones.Add(nuevaUbicacionRegistro);
                }
            }
            return preExtractos; // en ubicaciones tiene las que se quedan en registro para saber dónde se ha ubicado y también las que hay que modificar (lo hará el servicio).
        }
    }
}