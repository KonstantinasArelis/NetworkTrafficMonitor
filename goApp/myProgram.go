package main

import (
	"fmt"
	"time"
)

func fetchAPI(id int) {
	time.Sleep(1 * time.Second)
	fmt.Printf("Fetched %d\n", id)
}

func main () {
	go fetchAPI(1)
	go fetchAPI(2)

	time.Sleep(2 * time.Second)
	fmt.Println("Done")
}