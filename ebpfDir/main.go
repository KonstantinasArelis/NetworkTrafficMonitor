package main

import (
	"log"
	"net"
	"os"
	"os/signal"
	"time"

	"github.com/cilium/ebpf/link"
	"github.com/cilium/ebpf/rlimit"
)

func main() {
	// Remove memory limits for older kernels
	if err := rlimit.RemoveMemlock(); err != nil {
		log.Fatal("Removing memlock: ", err)
	}

	var objs counterObjects
	if err := loadCounterObjects(&objs, nil); err != nil {
		log.Fatal("Loading eBPF objects: ", err)
	}
	defer objs.Close()

	ifname := "lo" // will this work? lo is a fake nic
	iface, err := net.InterfaceByName(ifname)
	if err != nil {
		log.Fatal("Getting interfaces: ", ifname, err)
	}

	link, err := link.AttachXDP(link.XDPOptions{
		Program:   objs.CountPackets,
		Interface: iface.Index,
	})
	if err != nil {
		log.Fatal("Attaching XDP: ", err)
	}
	defer link.Close()

	log.Printf("Counting packets on %s..", ifname)

	// Periodically check counter from PktCount
	// exit when interupted
	tick := time.Tick(time.Second)
	stop := make(chan os.Signal, 5)
	signal.Notify(stop, os.Interrupt)
	for {
		select {
		case <-tick:
			var count uint64
			err := objs.PktCount.Lookup(uint32(0), &count)
			if err != nil {
				log.Fatal("Map lookup: ", err)
			}
			log.Printf("Received %d packets", count)
		case <-stop:
			log.Print("Received stop signal, exiting")
			return
		}
	}
}
