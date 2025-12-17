using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GestorUbicaciones
    {
        private LineaPedidoPicking linea;
        private List<UbicacionPicking> ubicaciones;

        public GestorUbicaciones(LineaPedidoPicking linea, List<UbicacionPicking> ubicaciones)
        {
            this.linea = linea;
            this.ubicaciones = ubicaciones;
        }

        public void Ejecutar()
        {
            bool continuar = true;
            int cantidadUbicada = 0;
            UbicacionPicking ubicacion;
            bool insertarUbicacion;

            if (linea.TipoLinea != Constantes.TiposLineaVenta.PRODUCTO)
            {
                return;
            }

            while (continuar)
            {
                ubicacion = ubicaciones.FirstOrDefault(u => u.Producto == linea.Producto && u.Estado == u.EstadoNuevo);
                if (ubicacion != null)
                {
                    insertarUbicacion = false;

                    if (linea.CantidadReservada - cantidadUbicada < ubicacion.CantidadNueva)
                    {
                        ubicacion.CantidadNueva = linea.CantidadReservada - cantidadUbicada;
                        insertarUbicacion = true;
                    }

                    cantidadUbicada += ubicacion.CantidadNueva;
                    ubicacion.LineaPedidoVentaId = linea.Id;
                    ubicacion.EstadoNuevo = Constantes.Ubicaciones.RESERVADO_PICKING;

                    continuar = cantidadUbicada < linea.CantidadReservada;

                    if (insertarUbicacion)
                    {
                        UbicacionPicking ubicacionNueva = new UbicacionPicking
                        {
                            CopiaId = ubicacion.Id != 0 ? ubicacion.Id : ubicacion.CopiaId,
                            Producto = ubicacion.Producto,
                            Cantidad = ubicacion.Cantidad - ubicacion.CantidadNueva,
                            CantidadNueva = ubicacion.Cantidad - ubicacion.CantidadNueva,
                            Estado = ubicacion.Estado,
                            EstadoNuevo = ubicacion.Estado
                        };
                        ubicaciones.Add(ubicacionNueva);
                        // Error temporal para ver donde se descuadran
                        if (ubicacionNueva.Estado == 3 && ubicacionNueva.LineaPedidoVentaId == 0)
                        {
                            throw new Exception("Descuadre en ubicación nueva. Id ubicación " + ubicacion.Id.ToString());
                        }
                    }

                    // Error temporal para ver donde se descuadran
                    if (ubicacion.Estado == 3 && ubicacion.LineaPedidoVentaId == 0)
                    {
                        throw new Exception("Descuadre en ubicación " + ubicacion.Id.ToString());
                    }



                    if (ubicacion.Id == 0 && ubicacion.CopiaId == 0)
                    {
                        throw new Exception("Error en los datos de las ubicaciones");
                    }


                } else
                {
                    // La ubicación está descuadrada
                    /*
                    UbicacionPicking ubicacionCuadre = new UbicacionPicking
                    {
                        CopiaId = 0,
                        Producto = linea.Producto,
                        Cantidad = linea.CantidadReservada - cantidadUbicada,
                        CantidadNueva = linea.CantidadReservada - cantidadUbicada,
                        Estado = Constantes.Ubicaciones.UBICADO,
                        EstadoNuevo = Constantes.Ubicaciones.PENDIENTE_UBICAR,
                        LineaPedidoVentaId = linea.Id
                    };
                    ubicaciones.Add(ubicacionCuadre);

                    continuar = false;
                    */
                    throw new Exception("La ubicación está descuadrada o el stock está en otra empresa" +
                        ". Producto: " + linea.Producto.Trim() +
                        ". Pedido: " + linea.NumeroPedido.ToString() +
                        ". Nº Orden Línea: " + linea.Id.ToString());
                }
            }
        }

        public static void Persistir(NVEntities db, List<UbicacionPicking> ubicaciones)
        {
            // Esta es la parte que no vamos a poder testar
            /*
             * Si Id = 0 hay que insertar una nueva db.Ubicaciones.add, copiando pasillo, fila, columna de CopiaId
             * Si cantidad es distinto a cantidadNueva hay que modificar y poner estado nuevo, NºOrdenLinPedidoVta
             * y PedidoVta (ojo que el pedido hay que leerlo de db.LinPedidoVtas).
             * */

            foreach(UbicacionPicking ubicacion in ubicaciones)
            {
                Ubicacion ubicacionOriginal;

                // Error cuando llega con Id == 0 y CopiaId == 0. Escribir test que falle y poner
                // un if (ubicacion.Id == 0 && ubicacion.CopiaId == 0), que inserte la ubicación.
                // Lo que está ahora irá en el else de ese if
                if (ubicacion.Id == 0) // && ubicacion.CopiaId != 0
                {
                    ubicacionOriginal = db.Ubicaciones.SingleOrDefault(u => u.NºOrden == ubicacion.CopiaId);
                    Ubicacion nuevaUbicacion = new Ubicacion
                    {
                        Almacén = ubicacionOriginal.Almacén,
                        Cantidad = ubicacion.CantidadNueva,
                        Empresa = ubicacionOriginal.Empresa,
                        Estado = ubicacion.EstadoNuevo,
                        Número = ubicacion.Producto,
                        PedidoCmp = ubicacionOriginal.PedidoCmp,
                        AlbaránCmp = ubicacionOriginal.AlbaránCmp,
                        NºOrdenCmp = ubicacionOriginal.NºOrdenCmp
                    };
                    
                    if (ubicacion.EstadoNuevo != Constantes.Ubicaciones.PENDIENTE_UBICAR)
                    {
                        nuevaUbicacion.Pasillo = ubicacionOriginal.Pasillo;
                        nuevaUbicacion.Fila = ubicacionOriginal.Fila;
                        nuevaUbicacion.Columna = ubicacionOriginal.Columna;
                    };

                    LinPedidoVta lineaVenta = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == ubicacion.LineaPedidoVentaId);
                    if (lineaVenta != null)
                    {
                        nuevaUbicacion.PedidoVta = lineaVenta.Número;
                        nuevaUbicacion.NºOrdenVta = ubicacion.LineaPedidoVentaId;
                    }

                    db.Ubicaciones.Add(nuevaUbicacion);

                    // Error temporal para ver donde se descuadran
                    if (nuevaUbicacion.Estado == 3 && (nuevaUbicacion.NºOrdenVta == 0 || nuevaUbicacion.NºOrdenVta == null))
                    {
                        throw new Exception("Descuadre en ubicación nueva. Id ubicación " + ubicacion.Id.ToString());
                    }
                } else
                {
                    ubicacionOriginal = db.Ubicaciones.SingleOrDefault(u => u.NºOrden == ubicacion.Id);
                    LinPedidoVta lineaVenta = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == ubicacion.LineaPedidoVentaId);
                    if (lineaVenta != null)
                    {
                        ubicacionOriginal.PedidoVta = lineaVenta.Número;
                        ubicacionOriginal.NºOrdenVta = ubicacion.LineaPedidoVentaId;
                    }
                    ubicacionOriginal.Estado = ubicacion.EstadoNuevo;
                    if (ubicacion.Cantidad != ubicacion.CantidadNueva)
                    {
                        ubicacionOriginal.Cantidad = ubicacion.CantidadNueva;
                    }

                    // Error temporal para ver donde se descuadran
                    if (ubicacionOriginal.Estado == 3 && (ubicacionOriginal.NºOrdenVta == 0 || ubicacionOriginal.NºOrdenVta == null))
                    {
                        throw new Exception("Descuadre en ubicación nueva. Id ubicación " + ubicacion.Id.ToString());
                    }
                }
            }
        }
    }
}